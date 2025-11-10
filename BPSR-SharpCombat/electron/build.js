#!/usr/bin/env node

const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');

function parseArgs() {
  const args = {};
  for (let i = 2; i < process.argv.length; i++) {
    const a = process.argv[i];
    if (a.startsWith('--')) {
      const [k, v] = a.substring(2).split('=');
      args[k] = v === undefined ? true : v;
    }
  }
  return args;
}

async function run(cmd, args, opts = {}) {
  return new Promise((resolve, reject) => {
    const p = spawn(cmd, args, { stdio: 'inherit', shell: true, ...opts });
    p.on('close', (code) => {
      if (code === 0) resolve(); else reject(new Error(`${cmd} exited ${code}`));
    });
    p.on('error', (err) => reject(err));
  });
}

async function copyRecursive(src, dest) {
  // Use fs.cp if available
  if (fs.cp) {
    await fs.promises.rm(dest, { recursive: true, force: true }).catch(() => {});
    await fs.promises.cp(src, dest, { recursive: true });
    return;
  }
  // Fallback
  const ncp = async (s, d) => {
    const entries = await fs.promises.readdir(s, { withFileTypes: true });
    await fs.promises.mkdir(d, { recursive: true });
    for (const entry of entries) {
      const srcPath = path.join(s, entry.name);
      const destPath = path.join(d, entry.name);
      if (entry.isDirectory()) await ncp(srcPath, destPath); else await fs.promises.copyFile(srcPath, destPath);
    }
  };
  await fs.promises.rm(dest, { recursive: true, force: true }).catch(() => {});
  await ncp(src, dest);
}

(async () => {
  try {
    const args = parseArgs();
    const repoRoot = path.resolve(__dirname, '..');
    // project file path (assumes csproj at repo root)
    const projectFile = path.join(repoRoot, 'BPSR-SharpCombat.csproj');
    const outServer = path.join(__dirname, 'server');
    const outApp = path.join(__dirname, 'app');

    // If packaging was requested but runtime/platform not supplied, choose sensible defaults
    if (args.package) {
      if (!args.publishRuntime) {
        if (process.platform === 'win32') args.publishRuntime = 'win-x64';
        else if (process.platform === 'linux') args.publishRuntime = 'linux-x64';
        else if (process.platform === 'darwin') args.publishRuntime = 'osx-x64';
      }
      if (!args.platform) {
        if (process.platform === 'win32') args.platform = 'win';
        else if (process.platform === 'linux') args.platform = 'linux';
        else if (process.platform === 'darwin') args.platform = 'darwin';
      }
    }

    if (args.devStart) {
      console.log('Dev start: launching dotnet run and electron (dev)');
      // Choose a stable dev URL
      const devUrl = process.env.APP_URL || 'http://localhost:5000';
      // Start dotnet run in project root with explicit --urls argument and ASPNETCORE_URLS set so it listens on the expected port
      const dotnetEnv = Object.assign({}, process.env, { ASPNETCORE_URLS: devUrl });
      const dotnetArgs = ['run', '--urls', devUrl];
      const dotnet = spawn('dotnet', dotnetArgs, { cwd: repoRoot, stdio: 'inherit', shell: true, env: dotnetEnv });
      // Run npm install then npm start in electron, passing APP_URL so Electron loads the same URL
      await run('npm', ['install'], { cwd: __dirname });
      await run('npm', ['start'], { cwd: __dirname, env: Object.assign({}, process.env, { APP_URL: devUrl }) });
      // When electron exits, kill dotnet
      dotnet.kill();
      return;
    }

    // Publish the .NET app if publishRuntime is provided
    if (args.publishRuntime) {
      const selfContained = args.selfContained === 'true' || args.selfContained === true;
      console.log(`Publishing .NET project ${projectFile} for runtime ${args.publishRuntime} (self-contained=${selfContained})`);
      await fs.promises.rm(outServer, { recursive: true, force: true }).catch(() => {});
      const publishArgs = ['publish', projectFile, '-c', 'Release', '-r', args.publishRuntime, `-o`, outServer];
      if (selfContained) publishArgs.push('--self-contained', 'true');
      // Do not bundle single file here to keep server folder readable
      await run('dotnet', publishArgs, { cwd: repoRoot });
    }

    // Copy wwwroot to electron/app for static mode
    const wwwroot = path.join(repoRoot, 'wwwroot');
    if (fs.existsSync(wwwroot)) {
      console.log('Copying wwwroot ->', outApp);
      await copyRecursive(wwwroot, outApp);
    } else {
      console.warn('wwwroot not found at', wwwroot);
    }

    // Run npm install in electron folder
    console.log('Running npm install in electron folder');
    await run('npm', ['install'], { cwd: __dirname });

    if (args.package) {
      const platform = args.platform || process.platform;
      // Map to electron-builder CLI flags
      if (platform.startsWith('win')) {
        console.log('Running electron-builder for Windows (nsis)');
        await run('npx', ['electron-builder', '--win'], { cwd: __dirname });
      } else if (platform.startsWith('linux')) {
        console.log('Running electron-builder for Linux (AppImage, deb)');
        await run('npx', ['electron-builder', '--linux'], { cwd: __dirname });
      } else {
        console.log('Running electron-builder for macOS');
        await run('npx', ['electron-builder', '--mac'], { cwd: __dirname });
      }
      console.log('Packaging complete');
      return;
    }

    console.log('Build complete. You can run `npm start` in electron/ to launch the app.');
  } catch (err) {
    console.error('Build failed:', err);
    process.exit(1);
  }
})();
