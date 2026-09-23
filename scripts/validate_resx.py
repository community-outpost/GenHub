#!/usr/bin/env python3
"""Validate .NET .resx localization files in CI.

Checks every ``Strings*.resx`` file under
``GenHub/GenHub/Resources/Localization``:

- each file is well-formed XML (a silently broken merge can never land);
- no resource key appears twice, including keys that differ only in case
  (MSBuild resource generation is case-insensitive on Windows);
- every satellite file carries exactly the same key set as the neutral
  ``Strings.resx`` (the repository's strict 1:1 parity rule);
- every translated value preserves the neutral file's ``{0}``-style
  placeholders.

Only the Python standard library is used. Exits 0 when everything passes,
1 with a grouped error report otherwise. Run from anywhere::

    python3 scripts/validate_resx.py
"""

import glob
import os
import re
import sys
import xml.etree.ElementTree as element_tree

PLACEHOLDER_RE = re.compile(r'\{\d+\}')
DATA_CLOSE = '</data>'
_MAX_LISTED = 10


def repo_localization_dir():
    scripts_dir = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(os.path.dirname(scripts_dir), 'GenHub', 'GenHub',
                        'Resources', 'Localization')


def load_entries(path, errors):
    """Return [(key, value)] or None when the file does not parse."""
    try:
        root = element_tree.parse(path).getroot()
    except element_tree.ParseError as exc:
        errors.append('%s: not well-formed XML: %s'
                      % (os.path.basename(path), exc))
        return None
    entries = []
    for node in root.iter('data'):
        entries.append((node.get('name'),
                        (node.findtext('value') or '')))
    nested = sorted({node.get('name') for node in root.iter('data')
                     if len(list(node.iter('data'))) > 1})
    if nested:
        shown = ', '.join(nested[:_MAX_LISTED])
        errors.append('%s: %d block(s) swallow following blocks as nested '
                      'children (missing %s?): %s'
                      % (os.path.basename(path), len(nested),
                         DATA_CLOSE, shown))
    return entries


def check_duplicates(path, entries, errors):
    seen = {}
    for key, _ in entries:
        if key in seen:
            errors.append('%s: duplicate key "%s"'
                          % (os.path.basename(path), key))
        else:
            seen[key] = True
    folded = {}
    for key, _ in entries:
        fold = (key or '').casefold()
        if fold in folded and folded[fold] != key:
            errors.append('%s: keys "%s" and "%s" differ only in case'
                          % (os.path.basename(path), folded[fold], key))
        else:
            folded.setdefault(fold, key)


def check_parity(neutral_name, neutral_keys, path, entries, errors):
    names = {key for key, _ in entries}
    for key in sorted(neutral_keys - names):
        if len([e for e in errors if 'missing key' in e]) >= _MAX_LISTED:
            break
        errors.append('%s: missing key "%s" (present in %s)'
                      % (os.path.basename(path), key, neutral_name))
    missing_count = len(neutral_keys - names)
    if missing_count > _MAX_LISTED:
        errors.append('%s: ... and %d more missing keys'
                      % (os.path.basename(path),
                         missing_count - _MAX_LISTED))
    for key in sorted(names - neutral_keys):
        errors.append('%s: extra key "%s" (absent from %s)'
                      % (os.path.basename(path), key, neutral_name))


def check_placeholders(neutral_name, neutral_values, path, entries, errors):
    reported = 0
    for key, value in entries:
        if key not in neutral_values:
            continue
        expected = sorted(set(PLACEHOLDER_RE.findall(neutral_values[key])))
        found = sorted(set(PLACEHOLDER_RE.findall(value)))
        if expected != found:
            errors.append('%s: key "%s" placeholders %s, expected %s '
                          'from %s' % (os.path.basename(path), key, found,
                                       expected, neutral_name))
            reported += 1
            if reported >= _MAX_LISTED:
                errors.append('%s: ... further placeholder mismatches hidden'
                              % os.path.basename(path))
                break


def main():
    directory = (sys.argv[1] if len(sys.argv) > 1
                 else repo_localization_dir())
    pattern = os.path.join(directory, 'Strings*.resx')
    files = sorted(glob.glob(pattern))
    errors = []
    if not files:
        print('validate_resx: no files matching %s' % pattern)
        return 1
    neutral_path = os.path.join(directory, 'Strings.resx')
    if neutral_path not in files:
        print('validate_resx: neutral Strings.resx not found in %s'
              % directory)
        return 1
    neutral_entries = load_entries(neutral_path, errors)
    if neutral_entries is None:
        return report(errors)
    check_duplicates(neutral_path, neutral_entries, errors)
    neutral_keys = {key for key, _ in neutral_entries}
    neutral_values = {key: value for key, value in neutral_entries}
    print('validate_resx: %s: %d keys'
          % (os.path.basename(neutral_path), len(neutral_keys)))
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
        print('validate_resx: %s: %d keys'
              % (os.path.basename(path), len(entries)))
    return report(errors)


def report(errors):
    if errors:
        print('validate_resx: FAILED with %d problem(s):' % len(errors))
        for error in errors:
            print('  - %s' % error)
        return 1
    print('validate_resx: all localization files are valid')
    return 0


if __name__ == '__main__':
    sys.exit(main())
