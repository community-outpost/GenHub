#!/usr/bin/env python3
"""Validate .NET .resx localization files in CI.

Checks every ``Strings*.resx`` file under
``GenHub/GenHub/Resources/Localization``:

- each file is well-formed XML with a <root> element;
- all <data> elements are direct children of <root> and have both a non-whitespace name
  attribute without leading/trailing whitespace and a <value> element;
- no resource key appears twice, including keys that differ only in case
  (MSBuild resource generation is case-insensitive on Windows);
- every satellite file carries exactly the same key set as the neutral
  ``Strings.resx`` (the repository's strict 1:1 parity rule);
- every translated value preserves the neutral file's composite formatting
  placeholders (e.g. {0}, {0:N2}, {0,-10}) and has balanced formatting braces.

Only the Python standard library is used. Exits 0 when everything passes,
1 with a grouped error report otherwise. Run from anywhere::

    python scripts/validate_resx.py  (Windows)
    python3 scripts/validate_resx.py (Linux/macOS)
"""

import glob
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
    return re.findall(r'\{(\d+)(?:,-?\d+)?(?::[^{}]*)?\}', unescaped)


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


def repo_localization_dir():
    scripts_dir = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(os.path.dirname(scripts_dir), 'GenHub', 'GenHub',
                        'Resources', 'Localization')


def load_entries(path, errors):
    """Return [(key, value)] or None when the file does not parse."""
    base_name = os.path.basename(path)
    try:
        root = element_tree.parse(path).getroot()
    except element_tree.ParseError as exc:
        errors.append(f'{base_name}: not well-formed XML: {exc}')
        return None

    if root.tag != 'root':
        errors.append(f'{base_name}: root element must be <root>, found <{root.tag}>')
        return None

    direct_data = [child for child in root if child.tag == 'data']
    all_data = list(root.iter('data'))
    if len(all_data) > len(direct_data):
        misplaced_count = len(all_data) - len(direct_data)
        errors.append(f'{base_name}: {misplaced_count} <data> element(s) are not direct children of <root>')

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

    entries = []
    for node in direct_data:
        name = node.get('name')
        if not name or not name.strip():
            errors.append(f'{base_name}: <data> element missing or whitespace-only name attribute')
            continue
        if name.strip() != name:
            errors.append(f'{base_name}: key "{name}" has leading or trailing whitespace')
            continue
        val_elem = node.find('value')
        if val_elem is None:
            errors.append(f'{base_name}: key "{name}" is missing a <value> element')
            continue
        value = val_elem.text or ''
        if not check_unbalanced_braces(value):
            errors.append(f'{base_name}: key "{name}" has unbalanced braces in value: {value!r}')
            continue
        entries.append((name, value))

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


def main():
    directory = (sys.argv[1] if len(sys.argv) > 1
                 else repo_localization_dir())
    pattern = os.path.join(directory, 'Strings*.resx')
    files = sorted(glob.glob(pattern))
    errors = []
    if not files:
        print(f'validate_resx: no files matching {pattern}')
        return 1
    neutral_path = os.path.join(directory, 'Strings.resx')
    if neutral_path not in files:
        print(f'validate_resx: neutral Strings.resx not found in {directory}')
        return 1
    neutral_entries = load_entries(neutral_path, errors)
    if neutral_entries is None:
        return report(errors)
    check_duplicates(neutral_path, neutral_entries, errors)
    neutral_keys = {key for key, _ in neutral_entries}
    neutral_values = dict(neutral_entries)
    print(f'validate_resx: {os.path.basename(neutral_path)}: {len(neutral_keys)} keys')
    for path in files:
        if path == neutral_path:
            continue
        entries = load_entries(path, errors)
        if entries is None:
            continue
        check_duplicates(path, entries, errors)
        check_parity(os.path.basename(neutral_path), neutral_keys,
                     path, entries, errors)
        check_placeholders(os.path.basename(neutral_path), neutral_values,
                           path, entries, errors)
        print(f'validate_resx: {os.path.basename(path)}: {len(entries)} keys')
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
