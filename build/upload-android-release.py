"""Attach an APK to an explicitly selected existing release after identity checks."""
import hashlib,json,os,pathlib,re,subprocess

def gh(*args):
    return subprocess.run(['gh',*args],check=True,capture_output=True,text=True).stdout

tag=os.environ['RELEASE_TAG'];version=os.environ['ANDROID_VERSION'];repo=os.environ['GITHUB_REPOSITORY']
if not re.fullmatch(r'(?:android-)?v'+re.escape(version),tag):
    raise SystemExit('Release tag must match the portable application version.')
release=json.loads(gh('api',f'repos/{repo}/releases/tags/{tag}'))
obj=json.loads(gh('api',f'repos/{repo}/git/ref/tags/{tag}'))['object']
for _ in range(4):
    if obj['type']=='commit':break
    if obj['type']!='tag':raise SystemExit('Unexpected release reference.')
    obj=json.loads(gh('api',f'repos/{repo}/git/tags/{obj["sha"]}'))['object']
if obj['type']!='commit' or obj['sha']!=os.environ['GITHUB_SHA']:
    raise SystemExit('The release tag must identify the exact APK build commit.')
folder=pathlib.Path('artifacts/TEMP/android-download');assets=sorted(folder.iterdir())
expected={f'MonolithHarness-Portable-v{version}-android-arm64.apk','SHA256SUMS-android.txt'}
if {x.name for x in assets}!=expected:raise SystemExit('Unexpected APK files.')
if {x['name'] for x in release['assets']}.intersection(expected):raise SystemExit('Refusing to replace existing Android assets.')
gh('release','upload',tag,'--repo',repo,*map(str,assets))
remote=json.loads(gh('api',f'repos/{repo}/releases/{release["id"]}'));index={x['name']:x for x in remote['assets']}
for file in assets:
    with file.open('rb') as f:sha=hashlib.file_digest(f,'sha256').hexdigest()
    item=index[file.name]
    if item['state']!='uploaded' or item['size']!=file.stat().st_size or item.get('digest')!='sha256:'+sha:
        raise SystemExit('Uploaded fingerprint mismatch: '+file.name)
print('Android APK and checksum manifest uploaded and verified.')
