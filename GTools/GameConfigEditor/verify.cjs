// Runs against disposable configuration files, never the working game's balance data.
const fs = require('node:fs');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const dll = path.resolve(process.argv[2] || path.join(__dirname, 'bin/Debug/net8.0/GameConfigEditor.dll'));

(async () => {
  fs.mkdirSync(path.join(root, '.build-check'), { recursive: true });
  const fixture = fs.mkdtempSync(path.join(root, '.build-check/config-verify-'));
  fs.cpSync(path.join(root, 'GConfig'), path.join(fixture, 'GConfig'), { recursive: true });
  const port = await new Promise(resolve => {
    const socket = net.createServer();
    socket.listen(0, '127.0.0.1', () => { const port = socket.address().port; socket.close(() => resolve(port)); });
  });
  const server = spawn('dotnet', [dll, '--contentRoot', __dirname, '--project-root', fixture, '--port', String(port)], { windowsHide: true, stdio: 'ignore' });
  let spawnError;
  server.on('error', error => { spawnError = error; });
  const base = `http://127.0.0.1:${port}`;
  try {
    let catalog;
    for (let attempt = 0; attempt < 80; attempt++) {
      if (spawnError) throw spawnError;
      try { catalog = await (await fetch(base + '/api/catalog')).json(); break; }
      catch { await new Promise(resolve => setTimeout(resolve, 100)); }
    }
    assert.ok(catalog, 'Configuration server did not start');
    assert.equal(catalog.files.length, 4);
    assert.equal((await fetch(base)).status, 200);
    assert.equal((await fetch(base + '/api/document?name=../README.md')).status, 404);
    const original = await (await fetch(base + '/api/document?name=BattleWeapon.json')).json();
    const info = JSON.parse(original.content);
    async function save(content, revision = original.revision, token = catalog.token, name = original.name) {
      return fetch(base + '/api/document', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Config-Token': token }, body: JSON.stringify({ name, content, revision }) });
    }
    assert.equal((await save(original.content, original.revision, 'wrong')).status, 403);
    assert.equal((await save('null')).status, 400);
    assert.equal((await save(JSON.stringify({ ...info, cycleTime: -1 }))).status, 400);
    assert.equal(fs.readFileSync(path.join(fixture, 'GConfig/BattleWeapon.json'), 'utf8'), original.content);
    const saved = await save(JSON.stringify({ ...info, damage: info.damage + 1 }));
    assert.equal(saved.status, 200);
    const result = await saved.json();
    assert.equal(JSON.parse(result.content).damage, info.damage + 1);
    assert.equal(fs.readFileSync(path.join(fixture, 'Assets/Resources/Config/BattleWeapon.json'), 'utf8'), result.content);
    assert.equal(fs.readFileSync(path.join(fixture, 'GConfig/BattleWeapon.json'), 'utf8'), result.content);
    assert.equal((await save(original.content)).status, 409, 'Stale edit overwrote a newer document');
    const mode = await (await fetch(base + '/api/document?name=Modes/battle.json')).json();
    assert.equal((await save(JSON.stringify({ ...JSON.parse(mode.content), components: ['world', 'clock', 'unknown'] }), mode.revision, catalog.token, mode.name)).status, 400);
    const weapons = await (await fetch(base + '/api/document?name=Weapons.json')).json();
    assert.equal((await save('{"weapons":[null]}', weapons.revision, catalog.token, weapons.name)).status, 400);
    const changed = JSON.parse(weapons.content); changed.weapons.push(changed.weapons[0]);
    assert.equal((await save(JSON.stringify(changed), weapons.revision, catalog.token, weapons.name)).status, 400);
    console.log('CONFIG_EDITOR_VERIFY_PASS: validation, isolated save/export, stale-write protection, file whitelist');
  } finally { server.kill(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
