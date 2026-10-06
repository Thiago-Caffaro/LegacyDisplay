"""Preserve resolved NuGet copyrights/source locations and bundled license texts offline."""
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def collect(application):
    assets = json.loads((ROOT / application / 'obj/project.assets.json').read_text(encoding='utf-8'))
    folders = [Path(path) for path in assets['packageFolders']]
    output = ROOT / 'artifacts' / ('agent' if application.startswith('windows-agent') else 'studio')
    licenses = output / 'licenses'
    licenses.mkdir(parents=True, exist_ok=True)
    for path in (ROOT / 'docs/licenses').glob('*.txt'):
        shutil.copy2(path, licenses / path.name)
    lines = ['LegacyDisplay: third-party components', '',
             'The libraries below are used unmodified. These notices do not assign a license to LegacyDisplay source.',
             'MPL source is available at each repository URL and commit below. Runtime .NET notices: licenses/dotnet-THIRD-PARTY-NOTICES.txt.', '']
    for key, info in sorted(assets['libraries'].items()):
        if info['type'] != 'package':
            continue
        directory = next((folder / info['path'] for folder in folders if (folder / info['path']).exists()))
        metadata = next(child for child in ET.parse(next(directory.glob('*.nuspec'))).getroot() if child.tag.split('}')[-1] == 'metadata')
        fields = {child.tag.split('}')[-1]: child for child in metadata}
        value = lambda name: fields[name].text or '' if name in fields else ''
        lines.extend([key, 'Authors: ' + value('authors'), 'Copyright: ' + value('copyright')])
        license_node = fields.get('license')
        if license_node is not None:
            lines.append('License: ' + (license_node.text or ''))
            if license_node.attrib.get('type') == 'file':
                path = directory / license_node.text
                shutil.copy2(path, licenses / (key.replace('/', '-') + '-' + path.name))
        else:
            lines.append('License URL: ' + value('licenseUrl'))
        repo = fields.get('repository')
        if repo is not None:
            lines.extend(['Source: ' + repo.attrib.get('url', ''), 'Commit: ' + repo.attrib.get('commit', '')])
        else:
            lines.append('Project: ' + value('projectUrl'))
        lines.append('')
    (output / 'THIRD-PARTY-NOTICES.txt').write_text('\n'.join(lines), encoding='utf-8')


if __name__ == '__main__':
    collect('windows-agent/LegacyDisplay.Agent')
    collect('windows-studio/LegacyDisplay.Studio')
