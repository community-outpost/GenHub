#!/usr/bin/env python3
"""Validate .NET .resx localization files in CI.

Discovers and validates all resource groups across the codebase:
- each file is well-formed XML with a <root> element;
- all <data> elements are direct children of <root> and have both a non-whitespace name
  attribute without leading/trailing whitespace and a <value> element;
- no resource key appears twice, including keys that differ only in case
  (MSBuild resource generation is case-insensitive on Windows);
- every satellite culture file carries exactly the same key set as its neutral
  counterpart (the repository's strict 1:1 parity rule);
- every translated value preserves the neutral file's composite formatting
  placeholders (e.g. {0}, {0:N2}, {0,-10}) and has balanced formatting braces.

Only the Python standard library is used. Exits 0 when everything passes,
1 with a grouped error report otherwise. Run from anywhere::

    python scripts/validate_resx.py  (Windows)
    python3 scripts/validate_resx.py (Linux/macOS)
"""

import os
import re
import sys
import xml.etree.ElementTree as element_tree

DATA_CLOSE = '</data>'
_MAX_LISTED = 10


def extract_placeholders(text):
    """Extract argument indices from .NET composite format string, ignoring escaped braces."""
    if not text:
        return []
    unescaped = re.sub(r'\{\{|\}\}', '', text)
    return re.findall(r'\{\d+(?:,-?\d+)?(?::[^{}]*)?\}', unescaped)


def check_unbalanced_braces(text):
    """Return True if single braces in composite format strings are properly balanced."""
    if not text:
        return True
    unescaped = re.sub(r'\{\{|\}\}', '', text)
    depth = 0
    for char in unescaped:
        if char == '{':
            depth += 1
            if depth > 1:
                return False
        elif char == '}':
            depth -= 1
            if depth < 0:
                return False
    return depth == 0


def repo_default_dir():
    scripts_dir = os.path.dirname(os.path.abspath(__file__))
    project_dir = os.path.join(os.path.dirname(scripts_dir), 'GenHub', 'GenHub')
    if os.path.isdir(project_dir):
        return project_dir
    cwd_project_dir = os.path.join(os.getcwd(), 'GenHub', 'GenHub')
    if os.path.isdir(cwd_project_dir):
        return cwd_project_dir
    if os.path.isdir(os.path.join(os.getcwd(), 'GenHub')):
        return os.getcwd()
    return os.path.dirname(scripts_dir)


def _check_structural_elements(root, base_name, errors):
    if root.tag != 'root':
        errors.append(f'{base_name}: root element must be <root>, found <{root.tag}>')
        return None
    direct_data = [child for child in root if child.tag == 'data']
    all_data = list(root.iter('data'))
    if len(all_data) > len(direct_data):
        misplaced_count = len(all_data) - len(direct_data)
        errors.append(f'{base_name}: {misplaced_count} <data> element(s) are not direct children of <root>')
    return direct_data


def _check_nested_data(direct_data, base_name, errors):
    nested = sorted({
        node.get('name') for node in direct_data
        if node.get('name') and len(list(node.iter('data'))) > 1
    })
    if nested:
        shown = ', '.join(nested[:_MAX_LISTED])
        errors.append(
            f'{base_name}: {len(nested)} block(s) swallow following blocks as nested '
            f'children (missing {DATA_CLOSE}?): {shown}'
        )


def _validate_data_node(node, base_name, errors):
    name = node.get('name')
    if not name or not name.strip():
        errors.append(f'{base_name}: <data> element missing or whitespace-only name attribute')
        return None
    if name.strip() != name:
        errors.append(f'{base_name}: key "{name}" has leading or trailing whitespace')
        return None
    val_elem = node.find('value')
    if val_elem is None:
        errors.append(f'{base_name}: key "{name}" is missing a <value> element')
        return None
    value = val_elem.text or ''
    if not check_unbalanced_braces(value):
        errors.append(f'{base_name}: key "{name}" has unbalanced braces in value: {value!r}')
        return None
    return name, value


def load_entries(path, errors):
    """Return [(key, value)] or None when the file does not parse."""
    base_name = os.path.basename(path)
    try:
        root = element_tree.parse(path).getroot()
    except element_tree.ParseError as exc:
        errors.append(f'{base_name}: not well-formed XML: {exc}')
        return None

    direct_data = _check_structural_elements(root, base_name, errors)
    if direct_data is None:
        return None

    _check_nested_data(direct_data, base_name, errors)

    entries = []
    for node in direct_data:
        parsed = _validate_data_node(node, base_name, errors)
        if parsed is not None:
            entries.append(parsed)

    return entries


def check_duplicates(path, entries, errors):
    base_name = os.path.basename(path)
    seen = {}
    for key, _ in entries:
        if key in seen:
            errors.append(f'{base_name}: duplicate key "{key}"')
        else:
            seen[key] = True
    folded = {}
    for key, _ in entries:
        fold = (key or '').casefold()
        if fold in folded and folded[fold] != key:
            errors.append(f'{base_name}: keys "{folded[fold]}" and "{key}" differ only in case')
        else:
            folded.setdefault(fold, key)


def check_parity(neutral_name, neutral_keys, path, entries, errors):
    base_name = os.path.basename(path)
    names = {key for key, _ in entries}
    missing = sorted(neutral_keys - names)
    for key in missing[:_MAX_LISTED]:
        errors.append(f'{base_name}: missing key "{key}" (present in {neutral_name})')
    if len(missing) > _MAX_LISTED:
        errors.append(f'{base_name}: ... and {len(missing) - _MAX_LISTED} more missing keys')
    for key in sorted(names - neutral_keys):
        errors.append(f'{base_name}: extra key "{key}" (absent from {neutral_name})')


def check_placeholders(neutral_name, neutral_values, path, entries, errors):
    base_name = os.path.basename(path)
    reported = 0
    for key, value in entries:
        if key not in neutral_values:
            continue
        expected = sorted(extract_placeholders(neutral_values[key]))
        found = sorted(extract_placeholders(value))
        if expected != found:
            errors.append(
                f'{base_name}: key "{key}" placeholders {found}, expected {expected} from {neutral_name}'
            )
            reported += 1
            if reported >= _MAX_LISTED:
                errors.append(f'{base_name}: ... further placeholder mismatches hidden')
                break


def _should_skip_dir(dir_name):
    return (
        dir_name in ('.git', 'bin', 'obj', 'TestResults', 'artifacts')
        or dir_name.endswith('.Tests')
        or dir_name.startswith('Test')
    )


def _is_culture_tag(tag):
    return bool(re.match(r'^[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]+)*$', tag))


def _split_stem(filename):
    stem = filename[:-5]
    if '.' in stem:
        prefix, culture = stem.rsplit('.', 1)
        if _is_culture_tag(culture):
            return prefix, culture
    return None, None


def _discover_neutrals_and_satellites(root, resx_files, errors):
    neutrals = []
    for filename in resx_files:
        prefix, _ = _split_stem(filename)
        if prefix is not None:
            if f'{prefix}.resx' not in resx_files:
                errors.append(f'{filename}: satellite culture file has no corresponding neutral {prefix}.resx in {root}')
            continue
        neutrals.append(filename)
    return neutrals


def _find_satellites_for_neutral(root, neutral_filename, resx_files):
    stem = neutral_filename[:-5]
    satellites = []
    for filename in resx_files:
        if filename == neutral_filename or not filename.startswith(f'{stem}.'):
            continue
        culture = filename[len(stem) + 1:-5]
        if _is_culture_tag(culture):
            satellites.append(os.path.join(root, filename))
    return satellites


def find_resource_groups(target_dir):
    """Find all .resx files grouped by neutral base file and its satellite culture files."""
    groups = {}
    errors = []
    search_dir = os.path.dirname(target_dir) if os.path.isfile(target_dir) else target_dir

    for root, dirs, files in os.walk(search_dir):
        dirs[:] = [d for d in dirs if not _should_skip_dir(d)]
        norm_root = root.replace('\\', '/')
        if '/GenHub.Tests' in norm_root or norm_root.startswith('GenHub.Tests'):
            continue
        resx_files = sorted([f for f in files if f.endswith('.resx')])
        if not resx_files:
            continue
        neutrals = _discover_neutrals_and_satellites(root, resx_files, errors)
        for neutral_f in neutrals:
            satellites = _find_satellites_for_neutral(root, neutral_f, resx_files)
            groups[os.path.join(root, neutral_f)] = satellites

    return groups, errors


def validate_group(neutral_path, satellite_paths, errors):
    """Validate a neutral resx file and all its satellite culture files."""
    neutral_name = os.path.basename(neutral_path)
    neutral_entries = load_entries(neutral_path, errors)
    if neutral_entries is None:
        return
    check_duplicates(neutral_path, neutral_entries, errors)
    neutral_keys = {key for key, _ in neutral_entries}
    neutral_values = dict(neutral_entries)
    print(f'validate_resx: {neutral_name}: {len(neutral_keys)} keys')

    for path in sorted(satellite_paths):
        sat_name = os.path.basename(path)
        entries = load_entries(path, errors)
        if entries is None:
            continue
        check_duplicates(path, entries, errors)
        check_parity(neutral_name, neutral_keys, path, entries, errors)
        check_placeholders(neutral_name, neutral_values, path, entries, errors)
        print(f'validate_resx: {sat_name}: {len(entries)} keys')


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else repo_default_dir()
    groups, errors = find_resource_groups(target)
    if not groups and not errors:
        print(f'validate_resx: no .resx files found in {target}')
        return 1
    for neutral_path, satellite_paths in sorted(groups.items()):
        validate_group(neutral_path, satellite_paths, errors)
    return report(errors)


def report(errors):
    if errors:
        print(f'validate_resx: FAILED with {len(errors)} problem(s):')
        for error in errors:
            print(f'  - {error}')
        return 1
    print('validate_resx: all localization files are valid')
    return 0


if __name__ == '__main__':
    sys.exit(main())
