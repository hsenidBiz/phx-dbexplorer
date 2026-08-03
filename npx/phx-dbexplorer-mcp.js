#!/usr/bin/env node
'use strict';

// Launcher for the Phx DB Explorer MCP server. Resolves the current OS/arch,
// downloads the matching self-contained binary from this repo's GitHub
// Releases on first run (cached per-version under the user's home dir), then
// execs it and hands off stdio so it can speak MCP directly to the client.

const { spawnSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const https = require('https');
const zlib = require('zlib');

const REPO = 'hsenidBiz/phx-dbexplorer';
const ASSEMBLY = 'PhxDbExplorer';
const USER_AGENT = 'phx-dbexplorer-mcp-npx';

function ridFor(platform, arch) {
  if (platform === 'win32') return 'win-x64';
  if (platform === 'linux') return 'linux-x64';
  if (platform === 'darwin') return arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
  throw new Error(`Unsupported platform/arch: ${platform}/${arch}`);
}

function httpGetFollowingRedirects(url, onResponse, redirectsLeft = 5) {
  https
    .get(url, { headers: { 'User-Agent': USER_AGENT } }, (res) => {
      const { statusCode, headers } = res;
      if (statusCode >= 300 && statusCode < 400 && headers.location) {
        res.resume();
        if (redirectsLeft <= 0) throw new Error('Too many redirects');
        return httpGetFollowingRedirects(headers.location, onResponse, redirectsLeft - 1);
      }
      onResponse(res);
    })
    .on('error', (err) => {
      throw err;
    });
}

function getJson(url) {
  return new Promise((resolve, reject) => {
    try {
      httpGetFollowingRedirects(url, (res) => {
        if (res.statusCode !== 200) {
          reject(new Error(`GET ${url} failed with ${res.statusCode}`));
          return;
        }
        let data = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => (data += chunk));
        res.on('end', () => {
          try {
            resolve(JSON.parse(data));
          } catch (err) {
            reject(err);
          }
        });
      });
    } catch (err) {
      reject(err);
    }
  });
}

function downloadFile(url, destPath) {
  return new Promise((resolve, reject) => {
    try {
      httpGetFollowingRedirects(url, (res) => {
        if (res.statusCode !== 200) {
          reject(new Error(`GET ${url} failed with ${res.statusCode}`));
          return;
        }
        const file = fs.createWriteStream(destPath);
        res.pipe(file);
        file.on('finish', () => file.close(() => resolve()));
        file.on('error', reject);
      });
    } catch (err) {
      reject(err);
    }
  });
}

// Minimal ZIP reader (stored + deflate entries only, no zip64) so extraction
// doesn't depend on which `tar` happens to be first on PATH — GNU tar (e.g.
// Git Bash's MSYS tar, often ahead of Windows' own bsdtar) can't read zip at
// all, and a Windows path's drive-letter colon confuses tar's remote-archive
// detection. Our release assets are small single-file archives, well under
// the 4GB zip64 threshold, so the plain 32-bit fields below are sufficient.
function extractZip(archivePath, destDir) {
  const buf = fs.readFileSync(archivePath);
  const EOCD_SIG = 0x06054b50;
  let eocdOffset = -1;
  for (let i = buf.length - 22; i >= 0; i--) {
    if (buf.readUInt32LE(i) === EOCD_SIG) {
      eocdOffset = i;
      break;
    }
  }
  if (eocdOffset === -1) throw new Error('Not a valid zip file (EOCD record not found)');

  const entryCount = buf.readUInt16LE(eocdOffset + 10);
  let offset = buf.readUInt32LE(eocdOffset + 16);

  for (let i = 0; i < entryCount; i++) {
    if (buf.readUInt32LE(offset) !== 0x02014b50) {
      throw new Error('Malformed zip central directory entry');
    }
    const method = buf.readUInt16LE(offset + 10);
    const compSize = buf.readUInt32LE(offset + 20);
    const nameLen = buf.readUInt16LE(offset + 28);
    const extraLen = buf.readUInt16LE(offset + 30);
    const commentLen = buf.readUInt16LE(offset + 32);
    const localHeaderOffset = buf.readUInt32LE(offset + 42);
    const name = buf.toString('utf8', offset + 46, offset + 46 + nameLen);

    const lfhNameLen = buf.readUInt16LE(localHeaderOffset + 26);
    const lfhExtraLen = buf.readUInt16LE(localHeaderOffset + 28);
    const dataStart = localHeaderOffset + 30 + lfhNameLen + lfhExtraLen;
    const compData = buf.subarray(dataStart, dataStart + compSize);

    if (!name.endsWith('/')) {
      let data;
      if (method === 0) data = compData;
      else if (method === 8) data = zlib.inflateRawSync(compData);
      else throw new Error(`Unsupported zip compression method ${method} for entry "${name}"`);

      const outPath = path.join(destDir, name);
      fs.mkdirSync(path.dirname(outPath), { recursive: true });
      fs.writeFileSync(outPath, data);
    }

    offset += 46 + nameLen + extraLen + commentLen;
  }
}

async function resolveVersion() {
  const pinned = process.env.PHX_DBEXPLORER_VERSION;
  if (pinned) return pinned.replace(/^v/, '');
  const release = await getJson(`https://api.github.com/repos/${REPO}/releases/latest`);
  if (!release || !release.tag_name) {
    throw new Error(
      `No releases found for ${REPO} yet. Push a "v*.*.*" tag to trigger the release workflow, ` +
        'or set PHX_DBEXPLORER_VERSION to pin a specific tag.'
    );
  }
  return release.tag_name.replace(/^v/, '');
}

async function ensureBinary(version, rid) {
  const isWindows = rid.startsWith('win');
  const ext = isWindows ? 'zip' : 'tar.gz';
  const exeName = isWindows ? `${ASSEMBLY}.exe` : ASSEMBLY;

  const cacheDir = path.join(os.homedir(), '.cache', 'phx-dbexplorer-mcp', version, rid);
  const exePath = path.join(cacheDir, exeName);
  if (fs.existsSync(exePath)) return exePath;

  fs.mkdirSync(cacheDir, { recursive: true });

  const assetName = `${ASSEMBLY}-${version}-${rid}.${ext}`;
  const url = `https://github.com/${REPO}/releases/download/v${version}/${assetName}`;
  const archivePath = path.join(cacheDir, assetName);

  process.stderr.write(`[phx-dbexplorer-mcp] Downloading ${assetName}...\n`);
  await downloadFile(url, archivePath);

  if (isWindows) {
    extractZip(archivePath, cacheDir);
  } else {
    // Linux/macOS releases are .tar.gz — every POSIX system ships a `tar`
    // that reads that format natively.
    const extract = spawnSync('tar', ['-xzf', archivePath, '-C', cacheDir], { stdio: 'inherit' });
    if (extract.status !== 0) {
      throw new Error(`Failed to extract ${assetName} (tar exited ${extract.status})`);
    }
  }
  fs.unlinkSync(archivePath);

  if (!isWindows) fs.chmodSync(exePath, 0o755);
  return exePath;
}

async function main() {
  const rid = ridFor(process.platform, process.arch);
  const version = await resolveVersion();
  const exePath = await ensureBinary(version, rid);

  const result = spawnSync(exePath, process.argv.slice(2), { stdio: 'inherit' });
  if (result.error) throw result.error;
  process.exit(result.status === null ? 1 : result.status);
}

main().catch((err) => {
  process.stderr.write(`[phx-dbexplorer-mcp] ${err.message}\n`);
  process.exit(1);
});
