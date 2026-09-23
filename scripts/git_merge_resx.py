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

import re
import sys

DATA_OPEN_RE = re.compile(r'<data\b[^>]*?\bname="([^"]+)"')
DATA_CLOSE = '</data>'
SELF_CLOSE = '/>'

EXIT_OK = 0
EXIT_CONFLICT = 1
EXIT_ERROR = 2

_UTF8_BOM = b'\xef\xbb\xbf'


class ResxError(Exception):
    """Raised when an input file is not a readable .resx document."""


def read_input(path):
    """Read a resx file, returning (lines, newline, has_bom, has_trailing_nl)."""
    try:
        with open(path, 'rb') as handle:
            raw = handle.read()
    except OSError as exc:
        raise ResxError("cannot read %s: %s" % (path, exc))
    has_bom = raw.startswith(_UTF8_BOM)
    body = raw[len(_UTF8_BOM):] if has_bom else raw
    try:
        text = body.decode('utf-8')
    except UnicodeDecodeError as exc:
        raise ResxError("cannot decode %s as UTF-8: %s" % (path, exc))
    newline = '\r\n' if '\r\n' in text else '\n'
    normalized = text.replace('\r\n', '\n')
    has_trailing_nl = normalized.endswith('\n')
    lines = normalized.split('\n')
    if has_trailing_nl:
        lines = lines[:-1]
    return lines, newline, has_bom, has_trailing_nl


def split_blocks(lines):
    """Split lines into (header, [(key, block_lines)], footer).

    A block runs from its ``<data ...>`` opening line through the line
    containing ``</data>``. Anything before the first block is the header
    (XML declaration plus ``<resheader>`` elements); anything after the last
    block is the footer (normally just ``</root>``).
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
        if SELF_CLOSE in lines[i] or DATA_CLOSE in lines[i]:
            end = i
        else:
            end = None
            j = i + 1
            while j < total:
                if DATA_CLOSE in lines[j]:
                    end = j
                    break
                if DATA_OPEN_RE.search(lines[j]) is not None:
                    raise ResxError(
                        'unclosed <data name="%s"> block '
                        '(next block starts before %s)' % (key, DATA_CLOSE))
                j += 1
            if end is None:
                raise ResxError(
                    'unclosed <data name="%s"> block at end of file' % key)
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
            raise ResxError(
                '%s contains the key "%s" more than once' % (path, key))
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
    if current == other:
        return current
    if base == current:
        return other
    if base == other:
        return current
    return None


def render_conflict(current, base, other):
    """Render one conflicting key with familiar merge markers."""
    lines = ['<<<<<<< current']
    if current is None:
        lines.append('(key deleted in current)')
    else:
        lines.extend(current)
    lines.append('||||||| ancestor')
    if base is None:
        lines.append('(key not present in ancestor)')
    else:
        lines.extend(base)
    lines.append('=======')
    if other is None:
        lines.append('(key deleted in other)')
    else:
        lines.extend(other)
    lines.append('>>>>>>> other')
    return lines


def merge_documents(base, current, other):
    """Merge three loaded documents.

    Returns (output_lines, had_conflict). Key identity is the exact resource
    name; names that differ only in case are treated as the same key because
    MSBuild resource generation is case-insensitive, so keeping both would
    silently violate the repository's key-uniqueness rule.
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
    surviving = resolve_keys(base['by_key'], current['by_key'],
                             other['by_key'])
    emitted_folds = set()
    for key in current['order']:
        fold = key.casefold()
        if fold in emitted_folds:
            continue
        group = surviving.get(fold)
        if group is None:
            continue
        emitted_folds.add(fold)
        chunks.extend(group)
        had_conflict = had_conflict or any(
            kind != 'lines' for kind, *_ in group)
    for key in other['order']:
        fold = key.casefold()
        if fold in emitted_folds:
            continue
        group = surviving.get(fold)
        if group is None:
            continue
        emitted_folds.add(fold)
        chunks.extend(group)
        had_conflict = had_conflict or any(
            kind != 'lines' for kind, *_ in group)
    if footer is None:
        chunks.append(('conflict-text', base['footer'],
                       current['footer'], other['footer']))
        had_conflict = True
    else:
        chunks.append(('lines', footer))
    output = []
    for chunk in chunks:
        kind = chunk[0]
        if kind == 'lines':
            output.extend(chunk[1])
        elif kind == 'conflict':
            output.extend(render_conflict(chunk[1], chunk[2], chunk[3]))
            had_conflict = True
        elif kind == 'conflict-group':
            output.append('<<<<<<< current')
            output.append('(keys differ only in case; '
                          'resource names are case-insensitive)')
            for name, cur, _, oth in chunk[1]:
                output.append('--- variant: %s ---' % name)
                output.extend(cur if cur is not None else oth or [])
            output.append('>>>>>>> other')
            had_conflict = True
        else:
            _, region_base, region_cur, region_oth = chunk
            output.append('<<<<<<< current')
            output.extend(region_cur)
            output.append('||||||| ancestor')
            output.extend(region_base)
            output.append('=======')
            output.extend(region_oth)
            output.append('>>>>>>> other')
    return output, had_conflict


def resolve_keys(base, current, other):
    """Resolve every surviving key, grouped by casefolded name.

    Returns {fold: [chunk]} where a chunk is ('lines', lines) or
    ('conflict', current_or_None, base_or_None, other_or_None). Groups with
    more than one distinct exact name are always conflicts.
    """
    groups = {}
    for key in list(current) + [k for k in other if k not in current]:
        groups.setdefault(key.casefold(), []).append(key)
    for key in list(base):
        groups.setdefault(key.casefold(), []).append(key)
    result = {}
    for fold, names in groups.items():
        exact = []
        for name in names:
            if name not in exact:
                exact.append(name)
        if len(exact) > 1:
            members = []
            for name in exact:
                members.append((name, current.get(name),
                                base.get(name), other.get(name)))
            result[fold] = [('conflict-group', members)]
            continue
        name = exact[0]
        base_block = base.get(name)
        current_block = current.get(name)
        other_block = other.get(name)
        if name not in current and name not in other:
            continue
        if current_block == other_block:
            if current_block is not None:
                result[fold] = [('lines', current_block)]
            continue
        if base_block == current_block:
            if other_block is not None:
                result[fold] = [('lines', other_block)]
            continue
        if base_block == other_block:
            if current_block is not None:
                result[fold] = [('lines', current_block)]
            continue
        result[fold] = [('conflict', current_block, base_block, other_block)]
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


def merge_files(ancestor_path, current_path, other_path):
    """Run the driver; returns EXIT_OK, EXIT_CONFLICT, or EXIT_ERROR."""
    try:
        base = load_document(ancestor_path)
        current = load_document(current_path)
        other = load_document(other_path)
    except ResxError as exc:
        sys.stderr.write('git_merge_resx: error: %s\n' % exc)
        return EXIT_ERROR
    output, had_conflict = merge_documents(base, current, other)
    try:
        write_output(current_path, output, current['newline'],
                     current['has_bom'], current['has_trailing_nl'])
    except OSError as exc:
        sys.stderr.write('git_merge_resx: cannot write %s: %s\n'
                         % (current_path, exc))
        return EXIT_ERROR
    if had_conflict:
        sys.stderr.write('git_merge_resx: conflicting blocks remain in %s\n'
                         % current_path)
        return EXIT_CONFLICT
    return EXIT_OK


def main(argv):
    if len(argv) == 2 and argv[1] == '--self-test':
        failures = run_self_test()
        return EXIT_ERROR if failures else EXIT_OK
    if len(argv) != 4:
        sys.stderr.write(
            'usage: git_merge_resx.py <ancestor> <current> <other>\n'
            '   or: git_merge_resx.py --self-test\n')
        return EXIT_ERROR
    return merge_files(argv[1], argv[2], argv[3])


_TEST_HEADER = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<root>',
    '  <resheader name="version">',
    '    <value>2.0</value>',
    '  </resheader>',
]
_TEST_FOOTER = ['</root>']


def _block(key, value, comment=None):
    lines = ['  <data name="%s" xml:space="preserve">' % key,
             '    <value>%s</value>' % value]
    if comment is not None:
        lines.append('    <comment>%s</comment>' % comment)
    lines.append('  </data>')
    return lines


def _doc(blocks):
    lines = list(_TEST_HEADER)
    for key, value, comment in blocks:
        lines.extend(_block(key, value, comment))
    lines.extend(_TEST_FOOTER)
    return '\n'.join(lines) + '\n'


def _doc_keys(text):
    import xml.etree.ElementTree as element_tree
    root = element_tree.fromstring(text.encode('utf-8'))
    return [node.get('name') for node in root.iter('data')]


def run_self_test():
    """Exercise the merge rules without needing git; returns failure count."""
    import os
    import tempfile
    import xml.etree.ElementTree as element_tree

    failures = []

    def check(name, base, current, other, expect_exit, verify):
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
            problems.append('exit %d, expected %d' % (code, expect_exit))
        else:
            try:
                problems.extend(verify(result) or [])
            except element_tree.ParseError as exc:
                problems.append('result is not well-formed XML: %s' % exc)
        if problems:
            failures.append(name)
            sys.stderr.write('FAIL %s: %s\n' % (name, '; '.join(problems)))
        else:
            sys.stdout.write('ok %s\n' % name)

    def expect_keys(*keys):
        def verify(result):
            found = _doc_keys(result)
            missing = [key for key in keys if key not in found]
            if missing:
                return ['missing keys: %s' % ', '.join(missing)]
            if '<<<<<<<' in result:
                return ['unexpected conflict markers']
            return []
        return verify

    a = [('K.A', 'a', None), ('K.B', 'b', None)]
    check('adjacent-inserts', _doc(a),
          _doc(a + [('K.C', 'c', None)]),
          _doc([('K.D', 'd', None)] + a),
          EXIT_OK, expect_keys('K.A', 'K.B', 'K.C', 'K.D'))

    check('identical-insert', _doc(a),
          _doc(a + [('K.C', 'c', None)]),
          _doc(a + [('K.C', 'c', None)]),
          EXIT_OK, expect_keys('K.A', 'K.B', 'K.C'))

    def verify_single_copy(result):
        if result.count('<data name="K.C"') != 1:
            return ['K.C appears %d times'
                    % result.count('<data name="K.C"')]
        return []
    check('identical-insert-single-copy', _doc(a),
          _doc(a + [('K.C', 'c', None)]),
          _doc(a + [('K.C', 'c', None)]),
          EXIT_OK, verify_single_copy)

    def verify_conflict_markers(result):
        if '<<<<<<< current' not in result or '>>>>>>> other' not in result:
            return ['conflict markers missing']
        return []
    check('same-key-different-text', _doc(a),
          _doc([('K.A', 'a', None), ('K.B', 'b-cur', None)]),
          _doc([('K.A', 'a', None), ('K.B', 'b-oth', None)]),
          EXIT_CONFLICT, verify_conflict_markers)

    check('delete-one-side', _doc(a),
          _doc([('K.A', 'a', None)]),
          _doc(a),
          EXIT_OK, expect_keys('K.A'))

    check('delete-vs-modify', _doc(a),
          _doc([('K.A', 'a', None)]),
          _doc([('K.A', 'a', None), ('K.B', 'b-oth', None)]),
          EXIT_CONFLICT, verify_conflict_markers)

    check('modify-one-side', _doc(a),
          _doc(a),
          _doc([('K.A', 'a', None), ('K.B', 'b-oth', None)]),
          EXIT_OK, expect_keys('K.A', 'K.B'))

    check('rename-plus-unrelated-add',
          _doc(a),
          _doc([('K.A', 'a', None), ('K.Renamed', 'b', None)]),
          _doc(a + [('K.E', 'e', None)]),
          EXIT_OK, expect_keys('K.A', 'K.Renamed', 'K.E'))

    def verify_no_resurrection(result):
        found = _doc_keys(result)
        if 'K.B' in found:
            return ['deleted key K.B was resurrected']
        return expect_keys('K.A', 'K.Renamed', 'K.E')(result)
    check('rename-no-resurrection',
          _doc(a),
          _doc([('K.A', 'a', None), ('K.Renamed', 'b', None)]),
          _doc(a + [('K.E', 'e', None)]),
          EXIT_OK, verify_no_resurrection)

    check('case-variant-add', _doc(a),
          _doc(a + [('K.C', 'c', None)]),
          _doc(a + [('k.c', 'c-oth', None)]),
          EXIT_CONFLICT, verify_conflict_markers)

    def verify_comment_kept(result):
        if '<comment>keep me</comment>' not in result:
            return ['winning comment was dropped']
        return []
    check('comment-preserved', _doc(a),
          _doc([('K.A', 'a', 'keep me'), ('K.B', 'b', None)]),
          _doc(a),
          EXIT_OK, verify_comment_kept)

    multi = [('K.A', 'line one\nline two', None), ('K.B', 'b', None)]
    check('multiline-value', _doc(a),
          _doc(multi),
          _doc(a + [('K.E', 'e', None)]),
          EXIT_OK, expect_keys('K.A', 'K.B', 'K.E'))

    current_text = _doc(a)
    check('no-op-byte-identical', current_text, current_text, current_text,
          EXIT_OK,
          lambda result: [] if result == current_text
          else ['no-op merge changed bytes'])

    def check_bom_crlf():
        name = 'bom-crlf-preserved'
        current_raw = (b'\xef\xbb\xbf'
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
            problems.append('exit %d, expected %d' % (code, EXIT_OK))
        if not result.startswith(b'\xef\xbb\xbf'):
            problems.append('BOM was dropped')
        if b'\r\n' not in result or result.replace(b'\r\n', b'').find(b'\n') != -1:
            problems.append('CRLF line endings were not preserved')
        try:
            keys = _doc_keys(result.decode('utf-8-sig'))
            if 'K.E' not in keys:
                problems.append('other-side key K.E missing')
        except element_tree.ParseError as exc:
            problems.append('result is not well-formed XML: %s' % exc)
        if problems:
            failures.append(name)
            sys.stderr.write('FAIL %s: %s\n' % (name, '; '.join(problems)))
        else:
            sys.stdout.write('ok %s\n' % name)

    check_bom_crlf()

    header_base = list(_TEST_HEADER)
    header_cur = ['<?xml version="1.0" encoding="utf-8"?>',
                  '<root>',
                  '  <!-- touched by current -->',
                  '  <resheader name="version">',
                  '    <value>2.0</value>',
                  '  </resheader>']
    header_oth = ['<?xml version="1.0" encoding="utf-8"?>',
                  '<root>',
                  '  <!-- touched by other -->',
                  '  <resheader name="version">',
                  '    <value>2.0</value>',
                  '  </resheader>']
    check('header-both-changed',
          '\n'.join(header_base + _TEST_FOOTER) + '\n',
          '\n'.join(header_cur + _TEST_FOOTER) + '\n',
          '\n'.join(header_oth + _TEST_FOOTER) + '\n',
          EXIT_CONFLICT, verify_conflict_markers)

    sys.stdout.write('self-test: %d failure(s)\n' % len(failures))
    return len(failures)


if __name__ == '__main__':
    sys.exit(main(sys.argv))

