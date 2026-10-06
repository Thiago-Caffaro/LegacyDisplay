"""Verify the published MiningRoutine command interface using isolated runtime files only."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--project', required=True)
parser.add_argument('--executable', help='Optional staged published executable for testing an update.')
args = parser.parse_args()
project = Path(args.project).resolve()
exe = Path(args.executable).resolve() if args.executable else project / 'publish/MiningController.Windows.exe'
assert exe.is_file(), 'Publish the MiningRoutine executable first.'
with tempfile.TemporaryDirectory(prefix='legacy-mining-commands-') as directory:
    data = Path(directory) / 'isolated runtime'
    for mode in ('auto', 'paused', 'force_mine'):
        subprocess.run([str(exe), '--data-directory', str(data), '--mode', mode], check=True, timeout=10)
        command = json.loads((data / 'command.json').read_text(encoding='utf-8-sig'))
        persisted = json.loads((data / 'mode.json').read_text(encoding='utf-8-sig'))
        assert command['command'] == mode and persisted['command'] == mode
    subprocess.run([str(exe), '--data-directory', str(data), '--exit-night'], check=True, timeout=10)
    assert json.loads((data / 'command.json').read_text(encoding='utf-8-sig'))['command'] == 'exit_night'
    assert json.loads((data / 'mode.json').read_text(encoding='utf-8-sig'))['command'] == 'force_mine'
    subprocess.run([str(exe), '--data-directory', str(data), '--quiet-mining'], check=True, timeout=10)
    assert json.loads((data / 'command.json').read_text(encoding='utf-8-sig'))['command'] == 'quiet_mining'
    assert json.loads((data / 'mode.json').read_text(encoding='utf-8-sig'))['command'] == 'force_mine'
print('PASS: published MiningRoutine accepts all five button commands in an isolated runtime; live controller untouched.')
