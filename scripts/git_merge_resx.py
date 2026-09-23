#!/usr/bin/env python3
"""Key-level 3-way merge driver for .NET .resx localization files.

Selected by the ``merge=resx`` attribute in .gitattributes and run
automatically by the Resx Auto Merge workflow
(.github/workflows/resx-auto-merge.yml), which configures the driver
before merging development into open pull requests. Nobody needs to
configure or invoke it by hand.

Git invokes the driver as ``script <ancestor> <current> <other>``. The merged
result is written back to ``<current>``. Exit codes: 0 means cleanly merged,
1 means conflicting blocks remain (written with familiar ``<<<<<<<`` markers
for manual resolution), 2 means an input file could not be read or parsed.

Why blocks instead of lines: git's built-in ``merge=union`` aligns insertions
from both sides at the same anchor line and can silently drop a ``</data>``
closing tag, producing malformed XML while reporting success. Merging whole
``<data name="...">...</data>`` blocks by key makes that impossible:
insertions on both sides are independent keys and both survive. Only a genuine
same-key disagreement stops the merge.

Only the Python standard library is used so the driver runs on every
developer machine and CI runner without extra dependencies.
"""

import os
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as element_tree

DATA_OPEN_RE = re.compile(r'<data\b[^>]*?\bname="([^"]+)"')
DATA_CLOSE = '</data>'
DATA_SELF_CLOSE_RE = re.compile(r'<data\b[^>]*?/>')

MARKER_CURRENT = '<<<<<<< current'
MARKER_ANCESTOR = '||||||| ancestor'
MARKER_SEPARATOR = '======='
MARKER_OTHER = '>>>>>>> other'

EXIT_OK = 0
EXIT_CONFLICT = 1
EXIT_ERROR = 2

_UTF8_BOM = b'\xef\xbb\xbf'

TEST_XML_DECL = '<?xml version="1.0" encoding="utf-8"?>'
TEST_ROOT_OPEN = '<root>'
TEST_ROOT_CLOSE = '</root>'
TEST_RESHEADER_OPEN = '  <resheader name="version">'
TEST_RESHEADER_VALUE = '    <value>2.0</value>'
TEST_RESHEADER_CLOSE = '  </resheader>'
KEY_RENAMED = 'K.Renamed'


class ResxError(Exception):
    """Raised when an input file is not a readable .resx document."""


def read_input(path):
    """Read a resx file, returning (lines, newline, has_bom, has_trailing_nl)."""
    try:
        with open(path, 'rb') as handle:
            raw = handle.read()
    except OSError as exc:
        raise ResxError(f'cannot read {path}: {exc}') from exc
    has_bom = raw.startswith(_UTF8_BOM)
    body = raw[len(_UTF8_BOM):] if has_bom else raw
    try:
        text = body.decode('utf-8')
    except UnicodeDecodeError as exc:
        raise ResxError(f'cannot decode {path} as UTF-8: {exc}') from exc
    newline = '\r\n' if '\r\n' in text else '\n'
    normalized = text.replace('\r\n', '\n')
    has_trailing_nl = normalized.endswith('\n')
    lines = normalized.split('\n')
    if has_trailing_nl:
        lines = lines[:-1]
    return lines, newline, has_bom, has_trailing_nl


def _find_block_end(lines, i, total, key):
    line = lines[i]
    if DATA_SELF_CLOSE_RE.search(line) is not None or DATA_CLOSE in line:
        return i
    for j in range(i + 1, total):
        if DATA_CLOSE in lines[j]:
            return j
        if DATA_OPEN_RE.search(lines[j]) is not None:
            raise ResxError(
                f'unclosed <data name="{key}"> block '
                f'(next block starts before {DATA_CLOSE})'
            )
    raise ResxError(f'unclosed <data name="{key}"> block at end of file')


def split_blocks(lines):
    """Split lines into (header, [(key, block_lines)], footer).

    A block runs from after the previous block through the line containing
    </data>. Preserves blank lines or comments between data blocks.
    """
    blocks = []
    first_start = None
    last_end = None
    i = 0
    total = len(lines)
    while i < total:
        match = DATA_OPEN_RE.search(lines[i])
        if match is None:
            i += 1
            continue
        key = match.group(1)
        if not key:
            raise ResxError('found a <data> block with an empty name')
        if first_start is None:
            first_start = i
            start = i
        else:
            start = last_end + 1
        end = _find_block_end(lines, i, total, key)
        blocks.append((key, lines[start:end + 1]))
        last_end = end
        i = end + 1
    if first_start is None:
        return list(lines), [], []
    return lines[:first_start], blocks, lines[last_end + 1:]


def load_document(path):
    """Load a resx file into (header, {key: block_lines}, [keys], footer)."""
    lines, newline, has_bom, has_trailing_nl = read_input(path)
    header, blocks, footer = split_blocks(lines)
    by_key = {}
    order = []
    for key, block in blocks:
        if key in by_key:
            raise ResxError(f'{path} contains the key "{key}" more than once')
        by_key[key] = block
        order.append(key)
    return {
        'header': header,
        'by_key': by_key,
        'order': order,
        'footer': footer,
        'newline': newline,
        'has_bom': has_bom,
        'has_trailing_nl': has_trailing_nl,
    }


def merge_snippet(base, current, other):
    """Three-way merge of an unordered line run; None when unresolvable."""
    if current == other or base == other:
        return current
    if base == current:
        return other
    return None


def merge_sections(base_sec, current_sec, other_sec, label):
    """Three-way merge of header or footer lines with conflict markers on disagreement."""
    merged = merge_snippet(base_sec, current_sec, other_sec)
    if merged is not None:
        return merged, False
    conflict = [
        f'{MARKER_CURRENT} ({label})',
        *current_sec,
        f'{MARKER_ANCESTOR} ({label})',
        *base_sec,
        MARKER_SEPARATOR,
        *other_sec,
        f'{MARKER_OTHER} ({label})',
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


def _resolve_single_key(name, base, current, other):
    """Three-way merge for a single key that appears with identical casing on all sides."""
    in_base = name in base['by_key']
    in_cur = name in current['by_key']
    in_oth = name in other['by_key']

    if in_cur and in_oth:
        cur_block = current['by_key'][name]
        oth_block = other['by_key'][name]
        if cur_block == oth_block:
            return cur_block, False
        if in_base:
            base_block = base['by_key'][name]
            if cur_block == base_block:
                return oth_block, False
            if oth_block == base_block:
                return cur_block, False
            return _format_conflict(base_block, cur_block, oth_block), True
        return _format_conflict([], cur_block, oth_block), True

    if in_cur and not in_oth:
        if in_base and current['by_key'][name] == base['by_key'][name]:
            return [], False
        if not in_base:
            return current['by_key'][name], False
        return _format_conflict(base['by_key'][name], current['by_key'][name], []), True

    if in_oth and not in_cur:
        if in_base and other['by_key'][name] == base['by_key'][name]:
            return [], False
        if not in_base:
            return other['by_key'][name], False
        return _format_conflict(base['by_key'][name], [], other['by_key'][name]), True

    return [], False


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
    return [(_format_conflict(base_lines, cur_lines, oth_lines), True)]


def _anchor_index(target_index, resolved, target_order):
    """Return the output index of the latest target key that has already been placed."""
    for key in reversed(target_order[:target_index]):
        fold = key.casefold()
        if fold in resolved:
            return resolved[fold]
    return -1


def _place_surviving_keys(ordered_folds, fold_to_chunks, current_order, other_order):
    """Insert surviving keys into a deterministic order with adjacent insertions paired."""
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
        raw = _UTF8_BOM + raw
    dir_name = os.path.dirname(os.path.abspath(path))
    temp_file = tempfile.NamedTemporaryFile(mode='wb', dir=dir_name, delete=False)
    temp_path = temp_file.name
    try:
        temp_file.write(raw)
        temp_file.flush()
        temp_file.close()
        os.replace(temp_path, path)
    except Exception:
        try:
            if os.path.exists(temp_path):
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
    if proc.returncode == 0:
        return EXIT_OK
    if proc.returncode > 0:
        return EXIT_CONFLICT
    return EXIT_ERROR


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
        write_output(current_path, output, current['newline'],
                     current['has_bom'], current['has_trailing_nl'])
    except OSError as exc:
        sys.stderr.write(f'git_merge_resx: cannot write {current_path}: {exc}\n')
        return EXIT_ERROR
    if had_conflict:
        sys.stderr.write(f'git_merge_resx: conflicting blocks remain in {current_path}\n')
        return EXIT_CONFLICT
    return EXIT_OK


def main(argv):
    if len(argv) == 2 and argv[1] == '--self-test':
        failures = run_self_test()
        return EXIT_ERROR if failures else EXIT_OK
    if len(argv) != 4:
        sys.stderr.write(
            'usage: git_merge_resx.py <ancestor> <current> <other>\n'
            '   or: git_merge_resx.py --self-test\n'
        )
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


def _run_merge_test(name, base, current, other, expect_exit, verify):
    paths = []
    try:
        for text in (base, current, other):
            handle = tempfile.NamedTemporaryFile(
                'w', suffix='.resx', delete=False, encoding='utf-8')
            handle.write(text)
            handle.close()
            paths.append(handle.name)
        code = merge_files(paths[0], paths[1], paths[2])
        with open(paths[1], 'r', encoding='utf-8') as handle:
            result = handle.read()
    finally:
        for path in paths:
            try:
                os.unlink(path)
            except OSError:
                pass
    problems = []
    if code != expect_exit:
        problems.append(f'exit {code}, expected {expect_exit}')
    else:
        try:
            problems.extend(verify(result) or [])
        except element_tree.ParseError as exc:
            problems.append(f'result is not well-formed XML: {exc}')
    if problems:
        sys.stderr.write(f"FAIL {name}: {'; '.join(problems)}\n")
        return False
    sys.stdout.write(f'ok {name}\n')
    return True


def _run_bom_crlf_test(a):
    name = 'bom-crlf-preserved'
    current_raw = (_UTF8_BOM
                   + _doc(a).replace('\n', '\r\n').encode('utf-8'))
    base_raw = _doc(a).encode('utf-8')
    other_raw = _doc(a + [('K.E', 'e', None)]).encode('utf-8')
    paths = []
    try:
        for raw in (base_raw, current_raw, other_raw):
            handle = tempfile.NamedTemporaryFile(
                'wb', suffix='.resx', delete=False)
            handle.write(raw)
            handle.close()
            paths.append(handle.name)
        code = merge_files(paths[0], paths[1], paths[2])
        with open(paths[1], 'rb') as handle:
            result = handle.read()
    finally:
        for path in paths:
            try:
                os.unlink(path)
            except OSError:
                pass
    problems = []
    if code != EXIT_OK:
        problems.append(f'exit {code}, expected {EXIT_OK}')
    if not result.startswith(_UTF8_BOM):
        problems.append('BOM was dropped')
    if b'\r\n' not in result or result.replace(b'\r\n', b'').find(b'\n') != -1:
        problems.append('CRLF line endings were not preserved')
    try:
        keys = _doc_keys(result.decode('utf-8-sig'))
        if 'K.E' not in keys:
            problems.append('other-side key K.E missing')
    except element_tree.ParseError as exc:
        problems.append(f'result is not well-formed XML: {exc}')
    if problems:
        sys.stderr.write(f"FAIL {name}: {'; '.join(problems)}\n")
        return False
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
        ('comment-preserved', _doc(a),
         _doc([('K.A', 'a', 'keep me'), ('K.B', 'b', None)]),
         _doc(a), EXIT_OK, _verify_comment_kept),
        ('multiline-value', _doc(a),
         _doc([('K.A', 'line one\nline two', None), ('K.B', 'b', None)]),
         _doc(a + [('K.E', 'e', None)]),
         EXIT_OK, _expect_keys('K.A', 'K.B', 'K.E')),
        ('no-op-byte-identical', _doc(a), _doc(a), _doc(a), EXIT_OK,
         lambda res: [] if res == _doc(a) else ['no-op merge changed bytes']),
    ]


def _run_header_merge_test():
    header_base = list(_TEST_HEADER)
    header_cur = [\
        TEST_XML_DECL,
        TEST_ROOT_OPEN,
        '  <!-- touched by current -->',
        TEST_RESHEADER_OPEN,
        TEST_RESHEADER_VALUE,
        TEST_RESHEADER_CLOSE,
    ]
    header_oth = [\
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
