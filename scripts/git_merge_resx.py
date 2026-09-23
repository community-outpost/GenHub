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
SELF_CLOSE = '/>'

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
    if SELF_CLOSE in line or DATA_CLOSE in line:
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


def render_conflict(current, base, other):
    """Render one conflicting key with familiar merge markers."""
    lines = [MARKER_CURRENT]
    if current is None:
        lines.append('(key deleted in current)')
    else:
        lines.extend(current)
    lines.append(MARKER_ANCESTOR)
    if base is None:
        lines.append('(key not present in ancestor)')
    else:
        lines.extend(base)
    lines.append(MARKER_SEPARATOR)
    if other is None:
        lines.append('(key deleted in other)')
    else:
        lines.extend(other)
    lines.append(MARKER_OTHER)
    return lines


def _collect_chunks(keys, surviving, emitted_folds):
    chunks = []
    had_conflict = False
    for key in keys:
        fold = key.casefold()
        if fold in emitted_folds:
            continue
        group = surviving.get(fold)
        if group is None:
            continue
        emitted_folds.add(fold)
        chunks.extend(group)
        if any(kind != 'lines' for kind, *_ in group):
            had_conflict = True
    return chunks, had_conflict


def _render_chunk(chunk):
    kind = chunk[0]
    if kind == 'lines':
        return chunk[1], False
    if kind == 'conflict':
        return render_conflict(chunk[1], chunk[2], chunk[3]), True
    if kind == 'conflict-group':
        lines = [
            MARKER_CURRENT,
            '(keys differ only in case; resource names are case-insensitive)',
        ]
        for name, cur, _, oth in chunk[1]:
            lines.append(f'--- variant: {name} ---')
            lines.extend(cur if cur is not None else oth or [])
        lines.append(MARKER_OTHER)
        return lines, True
    _, region_base, region_cur, region_oth = chunk
    lines = [
        MARKER_CURRENT,
        *region_cur,
        MARKER_ANCESTOR,
        *region_base,
        MARKER_SEPARATOR,
        *region_oth,
        MARKER_OTHER,
    ]
    return lines, True


def merge_documents(base, current, other):
    """Merge three loaded documents.

    Returns (output_lines, had_conflict).
    """
    header = merge_snippet(base['header'], current['header'], other['header'])
    footer = merge_snippet(base['footer'], current['footer'], other['footer'])
    chunks = []
    had_conflict = False
    if header is None:
        chunks.append(('conflict-text', base['header'],
                       current['header'], other['header']))
        had_conflict = True
    else:
        chunks.append(('lines', header))

    surviving = resolve_keys(base['by_key'], current['by_key'], other['by_key'])
    emitted_folds = set()
    cur_chunks, cur_conf = _collect_chunks(current['order'], surviving, emitted_folds)
    oth_chunks, oth_conf = _collect_chunks(other['order'], surviving, emitted_folds)
    chunks.extend(cur_chunks)
    chunks.extend(oth_chunks)
    had_conflict = had_conflict or cur_conf or oth_conf

    if footer is None:
        chunks.append(('conflict-text', base['footer'],
                       current['footer'], other['footer']))
        had_conflict = True
    else:
        chunks.append(('lines', footer))

    output = []
    for chunk in chunks:
        lines, conf = _render_chunk(chunk)
        output.extend(lines)
        if conf:
            had_conflict = True
    return output, had_conflict


def _group_keys_by_fold(base, current, other):
    groups = {}
    all_keys = list(current) + [k for k in other if k not in current]
    for key in all_keys:
        groups.setdefault(key.casefold(), []).append(key)
    for key in base:
        groups.setdefault(key.casefold(), []).append(key)
    return groups


def _resolve_single_key(base_block, current_block, other_block):
    if current_block == other_block or base_block == other_block:
        return [('lines', current_block)] if current_block is not None else []
    if base_block == current_block:
        return [('lines', other_block)] if other_block is not None else []
    return [('conflict', current_block, base_block, other_block)]


def _resolve_fold_group(names, base, current, other):
    exact = list(dict.fromkeys(names))
    if len(exact) > 1:
        members = [
            (name, current.get(name), base.get(name), other.get(name))
            for name in exact
        ]
        return [('conflict-group', members)]
    name = exact[0]
    if name not in current and name not in other:
        return []
    return _resolve_single_key(
        base.get(name), current.get(name), other.get(name)
    )


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
    """Write merged lines back using the current file's encoding habits."""
    text = newline.join(lines)
    if has_trailing_nl:
        text += newline
    raw = text.encode('utf-8')
    if has_bom:
        raw = _UTF8_BOM + raw
    with open(path, 'wb') as handle:
        handle.write(raw)


def fallback_line_merge(ancestor_path, current_path, other_path):
    """Write a standard diff3 merge into current so no side is lost."""
    try:
        subprocess.run(
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
    return EXIT_CONFLICT


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
