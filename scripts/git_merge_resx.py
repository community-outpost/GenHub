#!/usr/bin/env python3
"""Key-level 3-way merge driver for .NET .resx localization files.

Standard line-based merge tools (and git's built-in merge=union) frequently
break XML structure in .resx files when both branches add new resources:
adjacent insertions align on shared anchor lines and can silently drop a
</data> closing tag, producing invalid XML that still reports a clean merge.

This driver parses each file into:
- header lines (XML declaration, root open, resheader elements, comments);
- <data name="...">...</data> blocks keyed by resource name;
- footer lines (root close).

Merge rules:
- additions on one side only are kept;
- additions on both sides are both kept, with deterministic placement;
- identical edits on both sides merge cleanly (single copy);
- edits on one side only win;
- delete vs modify is a conflict;
- different edits to the same key produce standard <<<<<<< / ======= / >>>>>>>
  conflict markers wrapped around the conflicting <data> blocks;
- case-only collisions (e.g. key "foo" added on one side and "Foo" on the other)
  are flagged as conflicts because MSBuild resource generation is
  case-insensitive on Windows;
- non-<data> content (resheaders, comments, schema) is compared: if both sides
  touched it differently, standard conflict markers are emitted.

Fallback behavior:
- if any input cannot be parsed or validation of the merged output fails, the
  driver falls back to standard line-based 3-way merging (git merge-file --diff3).
  Review the result manually in that case.

Configured in .gitattributes via:
    *.resx merge=resx

And registered in git config via:
    git config merge.resx.driver "python scripts/git_merge_resx.py %O %A %B"
(use "python3" on Linux/macOS).

Exit codes (standard git merge driver contract):
    0: merge completed cleanly;
    1: conflicts left in current file;
    2: fatal error (abort the merge).

Only the Python standard library is used.
"""

import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as element_tree

EXIT_OK = 0
EXIT_CONFLICT = 1
EXIT_ERROR = 2

MARKER_CURRENT = '<<<<<<< current'
MARKER_ANCESTOR = '||||||| ancestor'
MARKER_SEPARATOR = '======='
MARKER_OTHER = '>>>>>>> other'

KEY_RENAMED = 'K.B-renamed'
TEST_XML_DECL = '<?xml version="1.0" encoding="utf-8"?>'
TEST_ROOT_OPEN = '<root>'
TEST_RESHEADER_OPEN = '  <resheader name="resmimetype">'
TEST_RESHEADER_VALUE = '    <value>text/microsoft-resx</value>'
TEST_RESHEADER_CLOSE = '  </resheader>'
TEST_ROOT_CLOSE = '</root>'
TEST_DATA_CLOSE = '</data>'

RE_DATA_OPEN = re.compile(
    r'''^\s*<data\b[^>]*?\bname\s*=\s*(["'])([^"']*)\1''',
    re.IGNORECASE,
)
RE_DATA_CLOSE = re.compile(r'</data>', re.IGNORECASE)


class ResxError(Exception):
    """Raised when a resx file cannot be parsed or validated."""


def detect_line_ending(raw_bytes):
    """Return the dominant line ending (CRLF or LF) as a string."""
    crlf_count = raw_bytes.count(b'\r\n')
    lf_only_count = raw_bytes.count(b'\n') - crlf_count
    return '\r\n' if crlf_count >= lf_only_count else '\n'


def load_raw_text(path):
    with open(path, 'rb') as handle:
        raw = handle.read()
    has_bom = raw.startswith(b'\xef\xbb\xbf')
    if has_bom:
        raw = raw[3:]
    newline = detect_line_ending(raw)
    try:
        text = raw.decode('utf-8')
    except UnicodeDecodeError as exc:
        raise ResxError(f'{path}: not valid UTF-8: {exc}') from exc
    return text, newline, has_bom


def _find_root_close(lines, path):
    for idx in range(len(lines) - 1, -1, -1):
        if '</root>' in lines[idx]:
            return idx
    raise ResxError(f'{path}: missing </root> element')


def _consume_enclosed_block(line, i, delim):
    end = line.find(delim, i)
    if end == -1:
        return len(line), True
    return end + len(delim), False


def _find_next_block_start(line, i):
    comment_pos = line.find('<!--', i)
    cdata_pos = line.find('<![CDATA[', i)
    if comment_pos != -1 and (cdata_pos == -1 or comment_pos < cdata_pos):
        return comment_pos, 4, '-->', True, False
    if cdata_pos != -1:
        return cdata_pos, 9, ']]>', False, True
    return -1, 0, None, False, False


def _strip_comments_and_cdata(line, in_comment, in_cdata):
    """Strip XML comments and CDATA sections from a line to expose structural XML tags."""
    active_delim = '-->' if in_comment else (']]>' if in_cdata else None)
    if active_delim:
        i, still_active = _consume_enclosed_block(line, 0, active_delim)
        if still_active:
            return '', in_comment, in_cdata
        in_comment = False
        in_cdata = False
    else:
        i = 0

    n = len(line)
    result = []
    while i < n:
        start_pos, offset, close_delim, is_comm, is_cd = _find_next_block_start(line, i)
        if start_pos == -1:
            result.append(line[i:])
            break
        result.append(line[i:start_pos])
        result.append(' ')
        i, still_active = _consume_enclosed_block(line, start_pos + offset, close_delim)
        if still_active:
            return ''.join(result), is_comm, is_cd
    return ''.join(result), False, False


def extract_header_and_body(lines, path):
    first_data_idx = None
    last_data_close = None
    in_comment = False
    in_cdata = False

    for idx, line in enumerate(lines):
        clean_line, in_comment, in_cdata = _strip_comments_and_cdata(line, in_comment, in_cdata)
        if first_data_idx is None and RE_DATA_OPEN.search(clean_line):
            first_data_idx = idx
        if first_data_idx is not None and RE_DATA_CLOSE.search(clean_line):
            last_data_close = idx

    if first_data_idx is None:
        close_idx = _find_root_close(lines, path)
        return list(lines[:close_idx]), list(lines[close_idx:]), []

    if last_data_close is None:
        raise ResxError(f'{path}: unclosed <data> element')

    footer_start = last_data_close + 1
    return list(lines[:first_data_idx]), list(lines[footer_start:]), list(lines[first_data_idx:footer_start])


def _handle_body_line(line, path, current_key, current_block, by_key, order, in_comment, in_cdata):
    clean_line, in_comment, in_cdata = _strip_comments_and_cdata(line, in_comment, in_cdata)
    match = RE_DATA_OPEN.search(clean_line)
    if match:
        if current_key is not None:
            raise ResxError(f'{path}: nested <data> tag at line: {line.strip()}')
        current_key = match.group(2)
        current_block = [line]
    elif current_key is not None:
        current_block.append(line)
    else:
        stripped = clean_line.strip()
        if stripped and not stripped.startswith('<!--'):
            raise ResxError(f'{path}: unexpected non-data content: {stripped}')
        return None, [], in_comment, in_cdata

    if RE_DATA_CLOSE.search(clean_line):
        _record_block(path, current_key, current_block, by_key, order)
        return None, [], in_comment, in_cdata

    return current_key, current_block, in_comment, in_cdata


def parse_data_blocks(body_lines, path):
    by_key = {}
    order = []
    current_key = None
    current_block = []
    in_comment = False
    in_cdata = False

    for line in body_lines:
        current_key, current_block, in_comment, in_cdata = _handle_body_line(
            line, path, current_key, current_block, by_key, order, in_comment, in_cdata
        )

    if current_key is not None:
        raise ResxError(f'{path}: unclosed <data name="{current_key}"> block at end of file')

    return by_key, order


def _record_block(path, key, block, by_key, order):
    if key in by_key:
        raise ResxError(f'{path}: duplicate resource key "{key}"')
    by_key[key] = block
    order.append(key)


def load_document(path):
    text, newline, has_bom = load_raw_text(path)
    has_trailing_nl = text.endswith('\r\n') or (not text.endswith('\r\n') and text.endswith('\n'))
    lines = text.splitlines()

    header, footer, body = extract_header_and_body(lines, path)
    by_key, order = parse_data_blocks(body, path)

    return {
        'path': path,
        'newline': newline,
        'has_bom': has_bom,
        'has_trailing_nl': has_trailing_nl,
        'header': header,
        'footer': footer,
        'by_key': by_key,
        'order': order,
    }


def merge_sections(base, current, other, label):
    if current == other or current == base:
        return other, False
    if other == base:
        return current, False
    conflict = [
        f'{MARKER_CURRENT} ({label})',
        *current,
        f'{MARKER_ANCESTOR} ({label})',
        *base,
        MARKER_SEPARATOR,
        *other,
        MARKER_OTHER,
    ]
    return conflict, True


def _group_keys_by_fold(base, current, other):
    """Collect keys into buckets keyed by casefolded name, keeping original case."""
    all_keys = (
        list(base['order'])
        + [k for k in current['order'] if k not in base['order']]
        + [k for k in other['order'] if k not in base['order'] and k not in current['order']]
    )
    groups = {}
    for key in all_keys:
        groups.setdefault(key.casefold(), []).append(key)
    return groups


def _format_conflict(base_lines, cur_lines, oth_lines):
    return [
        MARKER_CURRENT,
        *cur_lines,
        MARKER_ANCESTOR,
        *base_lines,
        MARKER_SEPARATOR,
        *oth_lines,
        MARKER_OTHER,
    ]


def _resolve_both_present(base_block, cur_block, oth_block):
    """Resolve a key present in both current and other."""
    if cur_block == oth_block:
        return cur_block, False
    if base_block is not None:
        if cur_block == base_block:
            return oth_block, False
        if oth_block == base_block:
            return cur_block, False
        return _format_conflict(base_block, cur_block, oth_block), True
    return _format_conflict([], cur_block, oth_block), True


def _resolve_one_sided(present_block, base_block, is_current):
    """Resolve a key present on only one side (added or other deleted)."""
    if base_block is not None:
        if present_block == base_block:
            return [], False
        cur_lines = present_block if is_current else []
        oth_lines = [] if is_current else present_block
        return _format_conflict(base_block, cur_lines, oth_lines), True
    return present_block, False


def _resolve_single_key(name, base, current, other):
    """Three-way merge for a single key that appears with identical casing on all sides."""
    base_block = base['by_key'].get(name)
    cur_block = current['by_key'].get(name)
    oth_block = other['by_key'].get(name)

    if cur_block is not None and oth_block is not None:
        return _resolve_both_present(base_block, cur_block, oth_block)
    if cur_block is not None:
        return _resolve_one_sided(cur_block, base_block, is_current=True)
    if oth_block is not None:
        return _resolve_one_sided(oth_block, base_block, is_current=False)
    return [], False


def _resolve_fold_group(names, base, current, other):
    """Resolve a casefold group. Multiple distinct casings produce a conflict."""
    if len(names) == 1:
        chunk, conflict = _resolve_single_key(names[0], base, current, other)
        return [(chunk, conflict)] if chunk else []
    cur_lines = []
    oth_lines = []
    base_lines = []
    for name in names:
        if name in current['by_key']:
            cur_lines.extend(current['by_key'][name])
        if name in other['by_key']:
            oth_lines.extend(other['by_key'][name])
        if name in base['by_key']:
            base_lines.extend(base['by_key'][name])
    if cur_lines == oth_lines:
        return [(cur_lines, False)]
    return [(_format_conflict(base_lines, cur_lines, oth_lines), True)]


def _anchor_index(target_index, resolved, target_order):
    """Return the output index of the latest target key that has already been placed."""
    for i in range(target_index - 1, -1, -1):
        prev = target_order[i]
        if prev in resolved:
            return resolved[prev]
    return -1


def _place_surviving_keys(ordered_folds, fold_to_chunks, current_order, other_order):
    """Preserve ancestor order for surviving base keys, then insert side-specific additions."""
    current_folds = [k.casefold() for k in current_order if k.casefold() in fold_to_chunks]
    other_folds = [k.casefold() for k in other_order if k.casefold() in fold_to_chunks]
    output = []
    resolved = {}

    def insert(fold, pos):
        output.insert(pos, fold)
        for idx in range(pos, len(output)):
            resolved[output[idx]] = idx

    for fold in ordered_folds:
        if fold in fold_to_chunks and fold not in resolved:
            insert(fold, len(output))

    for fold in current_folds:
        if fold in fold_to_chunks and fold not in resolved:
            pos = _anchor_index(current_folds.index(fold), resolved, current_folds) + 1
            insert(fold, pos)

    for fold in other_folds:
        if fold in fold_to_chunks and fold not in resolved:
            pos = _anchor_index(other_folds.index(fold), resolved, other_folds) + 1
            insert(fold, pos)

    return output


def merge_documents(base, current, other):
    """Three-way merge of loaded resx documents.

    Returns (merged_lines, had_conflict).
    """
    header, header_conflict = merge_sections(
        base['header'], current['header'], other['header'], 'header')
    footer, footer_conflict = merge_sections(
        base['footer'], current['footer'], other['footer'], 'footer')

    fold_to_chunks = resolve_keys(base, current, other)
    base_folds = [k.casefold() for k in base['order']]
    ordered_folds = [f for f in base_folds if f in fold_to_chunks]
    final_order = _place_surviving_keys(
        ordered_folds, fold_to_chunks, current['order'], other['order'])

    output = list(header)
    had_conflict = header_conflict or footer_conflict
    for fold in final_order:
        for chunk, conflict in fold_to_chunks[fold]:
            output.extend(chunk)
            if conflict:
                had_conflict = True
    output.extend(footer)
    return output, had_conflict


def resolve_keys(base, current, other):
    """Resolve every surviving key, grouped by casefolded name."""
    groups = _group_keys_by_fold(base, current, other)
    result = {}
    for fold, names in groups.items():
        chunks = _resolve_fold_group(names, base, current, other)
        if chunks:
            result[fold] = chunks
    return result


def write_output(path, lines, newline, has_bom, has_trailing_nl):
    """Write merged lines atomically using the current file's encoding habits."""
    text = newline.join(lines)
    if has_trailing_nl:
        text += newline
    raw = text.encode('utf-8')
    if has_bom:
        raw = b'\xef\xbb\xbf' + raw

    dir_name = os.path.dirname(os.path.abspath(path))
    temp_handle = tempfile.NamedTemporaryFile(
        mode='wb', dir=dir_name, delete=False, prefix='resx_merge_', suffix='.tmp'
    )
    temp_path = temp_handle.name
    try:
        temp_handle.write(raw)
        temp_handle.flush()
        temp_handle.close()
        os.replace(temp_path, path)
    except Exception:
        if os.path.exists(temp_path):
            try:
                os.remove(temp_path)
            except OSError:
                pass
        raise


def fallback_line_merge(ancestor_path, current_path, other_path):
    """Write a standard diff3 merge into current so no side is lost."""
    try:
        proc = subprocess.run(
            [
                'git', 'merge-file', '--diff3',
                '-L', 'current', '-L', 'ancestor', '-L', 'other',
                current_path, ancestor_path, other_path,
            ],
            check=False,
        )
    except OSError as exc:
        sys.stderr.write(f'git_merge_resx: fallback failed: {exc}\n')
        return EXIT_ERROR
    sys.stderr.write(
        f'git_merge_resx: fell back to a line merge for {current_path}; '
        'review the result manually\n'
    )
    if proc.returncode < 0 or proc.returncode > 127:
        return EXIT_ERROR
    if proc.returncode > 0:
        return EXIT_CONFLICT
    try:
        element_tree.parse(current_path)
    except element_tree.ParseError as exc:
        sys.stderr.write(
            f'git_merge_resx: line merge produced invalid XML for {current_path}: {exc}\n'
        )
        return EXIT_CONFLICT
    return EXIT_OK


def merge_files(ancestor_path, current_path, other_path):
    """Run the driver; returns EXIT_OK, EXIT_CONFLICT, or EXIT_ERROR."""
    try:
        base = load_document(ancestor_path)
        current = load_document(current_path)
        other = load_document(other_path)
    except ResxError as exc:
        sys.stderr.write(f'git_merge_resx: error: {exc}\n')
        return fallback_line_merge(ancestor_path, current_path, other_path)
    output, had_conflict = merge_documents(base, current, other)
    if not had_conflict:
        merged_xml = current['newline'].join(output)
        if current['has_trailing_nl']:
            merged_xml += current['newline']
        try:
            element_tree.fromstring(merged_xml.encode('utf-8'))
        except element_tree.ParseError as exc:
            sys.stderr.write(f'git_merge_resx: merged XML validation failed for {current_path}: {exc}\n')
            return fallback_line_merge(ancestor_path, current_path, other_path)
    try:
        write_output(
            current_path,
            output,
            current['newline'],
            current['has_bom'],
            current['has_trailing_nl'],
        )
    except OSError as exc:
        sys.stderr.write(f'git_merge_resx: cannot write output: {exc}\n')
        return EXIT_ERROR

    if had_conflict:
        sys.stderr.write(f'git_merge_resx: conflicting blocks remain in {current_path}\n')
        return EXIT_CONFLICT

    return EXIT_OK


def main(argv):
    if len(argv) == 2 and argv[1] in ('--self-test', '--test'):
        return run_self_test()
    if len(argv) != 4:
        sys.stderr.write('usage: git_merge_resx.py <ancestor> <current> <other>\n')
        sys.stderr.write('       git_merge_resx.py --self-test\n')
        return EXIT_ERROR
    return merge_files(argv[1], argv[2], argv[3])


_TEST_HEADER = [
    TEST_XML_DECL,
    TEST_ROOT_OPEN,
    TEST_RESHEADER_OPEN,
    TEST_RESHEADER_VALUE,
    TEST_RESHEADER_CLOSE,
]
_TEST_FOOTER = [TEST_ROOT_CLOSE]


def _block(key, value, comment=None):
    lines = [f'  <data name="{key}" xml:space="preserve">',
             f'    <value>{value}</value>']
    if comment is not None:
        lines.append(f'    <comment>{comment}</comment>')
    lines.append('  </data>')
    return lines


def _doc(blocks):
    lines = list(_TEST_HEADER)
    for key, value, comment in blocks:
        lines.extend(_block(key, value, comment))
    lines.extend(_TEST_FOOTER)
    return '\n'.join(lines) + '\n'


def _doc_keys(text):
    root = element_tree.fromstring(text.encode('utf-8'))
    return [node.get('name') for node in root.iter('data')]


def _write_temp(directory, name, content):
    path = os.path.join(directory, name)
    mode = 'wb' if isinstance(content, bytes) else 'w'
    encoding = None if isinstance(content, bytes) else 'utf-8'
    with open(path, mode, encoding=encoding) as handle:
        handle.write(content)
    return path


def _run_merge_test(name, base_text, cur_text, oth_text, exp_code, verifier):
    tmp = tempfile.mkdtemp(prefix='resx_test_')
    try:
        base_path = _write_temp(tmp, 'base.resx', base_text)
        cur_path = _write_temp(tmp, 'cur.resx', cur_text)
        oth_path = _write_temp(tmp, 'oth.resx', oth_text)
        code = merge_files(base_path, cur_path, oth_path)
        if code != exp_code:
            sys.stdout.write(f'FAIL {name}: exit code {code}, expected {exp_code}\n')
            return False
        with open(cur_path, 'r', encoding='utf-8') as handle:
            result = handle.read()
        errors = verifier(result)
        if errors:
            sys.stdout.write(f'FAIL {name}: {", ".join(errors)}\n')
            return False
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    sys.stdout.write(f'ok {name}\n')
    return True


def _expect_keys(*keys):
    def verify(result):
        found = _doc_keys(result)
        missing = [key for key in keys if key not in found]
        if missing:
            return [f"missing keys: {', '.join(missing)}"]
        if MARKER_CURRENT in result:
            return ['unexpected conflict markers']
        return []
    return verify


def _verify_single_copy(result):
    count = result.count('<data name="K.C"')
    return [f'K.C appears {count} times'] if count != 1 else []


def _verify_conflict_markers(result):
    if MARKER_CURRENT not in result or MARKER_OTHER not in result:
        return ['conflict markers missing']
    return []


def _verify_no_resurrection(result):
    found = _doc_keys(result)
    if 'K.B' in found:
        return ['deleted key K.B was resurrected']
    return _expect_keys('K.A', KEY_RENAMED, 'K.E')(result)


def _verify_comment_kept(result):
    if '<comment>keep me</comment>' not in result:
        return ['winning comment was dropped']
    return []


def _get_self_test_cases(a):
    return [
        ('adjacent-inserts', _doc(a), _doc(a + [('K.C', 'c', None)]),
         _doc([('K.D', 'd', None)] + a), EXIT_OK,
         _expect_keys('K.A', 'K.B', 'K.C', 'K.D')),
        ('identical-insert', _doc(a), _doc(a + [('K.C', 'c', None)]),
         _doc(a + [('K.C', 'c', None)]), EXIT_OK,
         _expect_keys('K.A', 'K.B', 'K.C')),
        ('identical-insert-single-copy', _doc(a), _doc(a + [('K.C', 'c', None)]),
         _doc(a + [('K.C', 'c', None)]), EXIT_OK, _verify_single_copy),
        ('same-key-different-text', _doc(a),
         _doc([('K.A', 'a', None), ('K.B', 'b-cur', None)]),
         _doc([('K.A', 'a', None), ('K.B', 'b-oth', None)]),
         EXIT_CONFLICT, _verify_conflict_markers),
        ('delete-one-side', _doc(a), _doc([('K.A', 'a', None)]),
         _doc(a), EXIT_OK, _expect_keys('K.A')),
        ('delete-vs-modify', _doc(a), _doc([('K.A', 'a', None)]),
         _doc([('K.A', 'a', None), ('K.B', 'b-oth', None)]),
         EXIT_CONFLICT, _verify_conflict_markers),
        ('modify-one-side', _doc(a), _doc(a),
         _doc([('K.A', 'a', None), ('K.B', 'b-oth', None)]),
         EXIT_OK, _expect_keys('K.A', 'K.B')),
        ('rename-plus-unrelated-add', _doc(a),
         _doc([('K.A', 'a', None), (KEY_RENAMED, 'b', None)]),
         _doc(a + [('K.E', 'e', None)]), EXIT_OK,
         _expect_keys('K.A', KEY_RENAMED, 'K.E')),
        ('rename-no-resurrection', _doc(a),
         _doc([('K.A', 'a', None), (KEY_RENAMED, 'b', None)]),
         _doc(a + [('K.E', 'e', None)]), EXIT_OK, _verify_no_resurrection),
        ('case-variant-add', _doc(a), _doc(a + [('K.C', 'c', None)]),
         _doc(a + [('k.c', 'c-oth', None)]),
         EXIT_CONFLICT, _verify_conflict_markers),
        ('case-rename-identical', _doc(a),
         _doc([('k.a', 'a', None), ('K.B', 'b', None)]),
         _doc([('k.a', 'a', None), ('K.B', 'b', None)]),
         EXIT_OK, _expect_keys('k.a', 'K.B')),
        ('cdata-and-comment-embedded-closing-tag', _doc(a),
         _doc([('K.A', '<![CDATA[val </data> inside]]>', 'keep <!-- </data> -->'), ('K.B', 'b', None)]),
         _doc(a + [('K.C', 'c', None)]),
         EXIT_OK, _expect_keys('K.A', 'K.B', 'K.C')),
        ('comment-preserved', _doc(a),
         _doc([('K.A', 'a', 'keep me'), ('K.B', 'b', None)]),
         _doc(a), EXIT_OK, _verify_comment_kept),
        ('multiline-value', _doc(a),
         _doc([('K.A', 'line one\nline two', None), ('K.B', 'b', None)]),
         _doc(a + [('K.E', 'e', None)]),
         EXIT_OK, _expect_keys('K.A', 'K.B', 'K.E')),
        ('no-op-byte-identical', _doc(a), _doc(a), _doc(a), EXIT_OK,
         lambda res: [] if res == _doc(a) else ['no-op merge changed bytes']),
        ('attribute-order-independent',
         _doc(a),
         _doc(a).replace('<data name="K.B" xml:space="preserve">', '<data xml:space="preserve" name="K.B">'),
         _doc(a + [('K.C', 'c', None)]),
         EXIT_OK, _expect_keys('K.A', 'K.B', 'K.C')),
        ('corrupt-input-fallback',
         _doc(a).replace(TEST_DATA_CLOSE, ''),
         _doc([('K.A', 'a-cur', None), ('K.B', 'b', None)]).replace(TEST_DATA_CLOSE, ''),
         _doc([('K.A', 'a-oth', None), ('K.B', 'b', None)]).replace(TEST_DATA_CLOSE, ''),
         EXIT_CONFLICT, _verify_conflict_markers),
    ]


def _run_header_merge_test():
    header_base = list(_TEST_HEADER)
    header_cur = [
        TEST_XML_DECL,
        TEST_ROOT_OPEN,
        '  <!-- touched by current -->',
        TEST_RESHEADER_OPEN,
        TEST_RESHEADER_VALUE,
        TEST_RESHEADER_CLOSE,
    ]
    header_oth = [
        TEST_XML_DECL,
        TEST_ROOT_OPEN,
        '  <!-- touched by other -->',
        TEST_RESHEADER_OPEN,
        TEST_RESHEADER_VALUE,
        TEST_RESHEADER_CLOSE,
    ]
    return _run_merge_test(
        'header-both-changed',
        '\n'.join(header_base + _TEST_FOOTER) + '\n',
        '\n'.join(header_cur + _TEST_FOOTER) + '\n',
        '\n'.join(header_oth + _TEST_FOOTER) + '\n',
        EXIT_CONFLICT,
        _verify_conflict_markers,
    )


def _run_bom_crlf_test(a):
    bom_raw = b'\xef\xbb\xbf' + _doc(a).replace('\n', '\r\n').encode('utf-8')
    cur_raw = b'\xef\xbb\xbf' + _doc(a + [('K.C', 'c', None)]).replace('\n', '\r\n').encode('utf-8')
    oth_raw = b'\xef\xbb\xbf' + _doc([('K.D', 'd', None)] + a).replace('\n', '\r\n').encode('utf-8')
    tmp = tempfile.mkdtemp(prefix='resx_test_')
    try:
        base_path = _write_temp(tmp, 'base.resx', bom_raw)
        cur_path = _write_temp(tmp, 'cur.resx', cur_raw)
        oth_path = _write_temp(tmp, 'oth.resx', oth_raw)
        code = merge_files(base_path, cur_path, oth_path)
        if code != EXIT_OK:
            sys.stdout.write(f'FAIL bom-crlf-preserved: exit code {code}\n')
            return False
        with open(cur_path, 'rb') as handle:
            res_bytes = handle.read()
        if not res_bytes.startswith(b'\xef\xbb\xbf'):
            sys.stdout.write('FAIL bom-crlf-preserved: BOM lost\n')
            return False
        body_no_bom = res_bytes[3:]
        if b'\r\n' not in body_no_bom or (body_no_bom.count(b'\n') != body_no_bom.count(b'\r\n')):
            sys.stdout.write('FAIL bom-crlf-preserved: CRLF not preserved\n')
            return False
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    sys.stdout.write('ok bom-crlf-preserved\n')
    return True


def run_self_test():
    """Exercise the merge rules without needing git; returns failure count."""
    failures = []
    a = [('K.A', 'a', None), ('K.B', 'b', None)]

    for name, base_text, cur_text, oth_text, exp_code, verifier in _get_self_test_cases(a):
        if not _run_merge_test(name, base_text, cur_text, oth_text, exp_code, verifier):
            failures.append(name)

    if not _run_bom_crlf_test(a):
        failures.append('bom-crlf-preserved')

    if not _run_header_merge_test():
        failures.append('header-both-changed')

    sys.stdout.write(f'self-test: {len(failures)} failure(s)\n')
    return len(failures)


if __name__ == '__main__':
    sys.exit(main(sys.argv))
