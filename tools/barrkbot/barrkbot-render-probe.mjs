/**
 * Feed Wonderland's barrkbot_wonderland.json samples through the REAL BarrkBOT reader (6.1.5 API: the server
 * registry entry is the first argument of every reader call; with VALHEIM_SERVERS_FILE='' the bot synthesises a
 * `default` server from VALHEIM_SERVER_DIR, which is what this probe uses).
 *
 * Run from /home/rohan/WubarrkCODING/WindowsDEV/Discord-BarrkBOT (dotenv loads its .env for config.js):
 *   /usr/bin/node <this file> [populated|fresh|players12|all|/path/to/barrkbot_wonderland.json]
 * BARRKBOT_DIR=<dir> points it at another checkout of the bot (e.g. a `git archive` of a tagged release).
 * A path runs that one file (once the mod has run on a server, feed the real export); a scenario name
 * runs the committed sample of that shape. The samples are generated to BarrkBotExport.Build()'s exact
 * key set and order - regenerate them when the serializer changes, do not hand-edit.
 *
 * Staging is the real path, not a hand-built cache: a temp VALHEIM_SERVER_DIR holds
 * BepInEx/config/Wonderland/barrkbot_wonderland.json, syncModExports() discovers + parses + caches it
 * exactly as the scheduler's sweep does, then readModExport / playerAcrossExports / listModExports
 * (get_valheim_mod_data) and valheimRecords (standings / leaderLines / detectChanges / headlineFor)
 * are called on that cache.
 */
process.env.BARRKBOT_TEST_ISOLATION = '1';
import { mkdirSync, rmSync, writeFileSync, copyFileSync, readFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const BOT = process.env.BARRKBOT_DIR || '/home/rohan/WubarrkCODING/WindowsDEV/Discord-BarrkBOT';
const box = join(HERE, `probe-box-${process.pid}`);
const serverDir = join(box, 'server');
const dataDir = join(box, 'data');
process.env.BARRKBOT_DATA_DIR = dataDir;
process.env.VALHEIM_SERVER_DIR = serverDir;
process.env.VALHEIM_SERVERS_FILE = ''; // no registry file: the bot synthesises the `default` server from VALHEIM_SERVER_DIR
process.env.LOCAL_TOOL_RESULT_CHARS = process.env.LOCAL_TOOL_RESULT_CHARS || '6000';
process.chdir(BOT);

// Import before creating the scratch box: an import failure (wrong BARRKBOT_DIR, mid-refactor tree) must not
// leave a probe-box-<pid>/ behind in the repo.
const { syncModExports, listCachedExports } = await import(`${BOT}/src/actions/valheimModData.js`);
const { readModExport, playerAcrossExports, listModExports, STALE_AFTER_MINUTES } = await import(`${BOT}/src/actions/valheimModExports.js`);
const { standings, leaderLines, detectChanges, headlineFor, recordFacts, celebrated, seed } = await import(`${BOT}/src/actions/valheimRecords.js`);
const { primaryServer } = await import(`${BOT}/src/servers.js`);
mkdirSync(join(serverDir, 'BepInEx', 'config', 'Wonderland'), { recursive: true });
mkdirSync(dataDir, { recursive: true });
const SERVER = primaryServer(); // 6.1.5: every reader call takes the server first

const SAMPLES = {
  populated: join(HERE, 'sample_populated.json'),
  fresh: join(HERE, 'sample_fresh.json'),
  players12: join(HERE, 'sample_12players.json'),
};
const arg = process.argv[2];
if (arg && arg !== 'all' && !SAMPLES[arg]) {
  if (!existsSync(arg)) { console.error(`no such scenario or file: ${arg} (scenarios: ${Object.keys(SAMPLES).join(', ')})`); process.exit(2); }
  SAMPLES.file = arg;
}
const want = arg && arg !== 'all' ? [SAMPLES[arg] ? arg : 'file'] : Object.keys(SAMPLES);
const CAP = Number(process.env.LOCAL_TOOL_RESULT_CHARS);
const hr = (t) => console.log(`\n${'='.repeat(100)}\n${t}\n${'='.repeat(100)}`);
const sub = (t) => console.log(`\n--- ${t} ---`);
const show = (v) => console.log(JSON.stringify(v, null, 2));

async function stage(name) {
  const target = join(serverDir, 'BepInEx', 'config', 'Wonderland', 'barrkbot_wonderland.json');
  copyFileSync(SAMPLES[name], target);
  // also drop the mod's own registry beside the config, exactly where the mod writes it, to prove the
  // sweep ignores it (name does not match /^barrkbot[_-].*\.json$/i), and a .tmp mid-write
  writeFileSync(join(serverDir, 'BepInEx', 'config', 'Wonderland.BarrkBot.VanillaBean01.dat'), '{"schema_version":1}');
  writeFileSync(target + '.tmp', '{"half": ');
  const r = await syncModExports(SERVER, { force: true });
  return r;
}

try {
for (const name of want) {
  hr(`SCENARIO: ${name}  (${SAMPLES[name]})`);
  const data = JSON.parse(readFileSync(SAMPLES[name], 'utf8'));
  const NOW = new Date(Date.parse(data.generated_at) + 90_000); // worst-case cache age: interval + one tick
  const sync = await stage(name);
  sub('sweep (syncModExports) result');
  show({ changed: sync.changed.map((c) => ({ path: c.path, mod: c.mod, name: c.name, rows: c.rows })), errors: sync.errors, scannedDirs: sync.scannedDirs, total: sync.total });
  sub('manifest entries (listCachedExports)');
  show(listCachedExports(SERVER).map((f) => ({ path: f.path, mod: f.mod, name: f.name, rows: f.rows, generatedAt: f.generatedAt })));

  sub('get_valheim_mod_data() with NO arguments -> listModExports');
  show(listModExports(SERVER, { now: NOW }));

  sub('get_valheim_mod_data({mod:"Wonderland"}) -> readModExport  [THE MOD OVERVIEW]');
  const overview = readModExport(SERVER, { mod: 'Wonderland', now: NOW });
  const size = JSON.stringify(overview).length;
  console.log(`serialized size: ${size} chars vs cap ${CAP} -> ${size <= CAP ? 'FITS' : 'OVER CAP (chat.js would hard-slice it)'}`);
  show(overview);

  sub('routing by content: mod:"bosses", mod:"coal", mod:"deaths", mod:"playtime", mod:"online"');
  for (const needle of ['bosses', 'coal', 'deaths', 'playtime', 'online']) {
    const r = readModExport(SERVER, { mod: needle, now: NOW });
    console.log(`  ${needle.padEnd(9)} -> ${r.found ? (r.redirect ? `REDIRECT to ${r.redirect}` : `found ${r.exports?.map((e) => e.file).join(',') ?? r.file}`) : 'NOT FOUND: ' + r.note.slice(0, 80)}`);
  }

  const data0 = data; // the sample this scenario staged
  const someone = Object.values(data0.players ?? {})[0]?.name ?? 'Rohan';
  if (Object.keys(data0.players ?? {}).length > 0) {
    sub(`"how long has ${someone} played" -> playerAcrossExports({player:"${someone}"})`);
    show(playerAcrossExports(SERVER, { player: someone, now: NOW }));
    sub('"who has died the most" -> the pre-sorted answer the reader hands the model');
    const p = overview.exports?.[0]?.collections?.players;
    console.log('topAnswer:', p?.topAnswer);
    console.log('leaders.deaths_alltime:', JSON.stringify(p?.leaders?.deaths_alltime ?? '(leaders table dropped, topAnswer carries it)'));
    sub('"which bosses are dead" -> the server block as rendered');
    show(overview.exports?.[0]?.collections?.server);
    sub('"how much coal has been fed" -> the lifetime block as rendered');
    show(overview.exports?.[0]?.collections?.lifetime);
  } else {
    sub('fresh server: player lookup for someone who has never been recorded');
    show(playerAcrossExports(SERVER, { player: 'Rohan', now: NOW }));
  }

  sub('notes the model receives (author notes + reader-generated), in order');
  for (const n of overview.exports?.[0]?.notes ?? []) console.log(' *', n);

  sub('valheimRecords: standings the achievements module derives from this file');
  const st = standings(SERVER, { now: NOW });
  for (const s of st) console.log(`  ${s.collection}.${s.field}: holder=${s.holder} value=${s.value} contenders=${s.contenders} declared=${s.declared} celebrated=${JSON.stringify(celebrated(s.field, s.declared))}`);
  sub('valheimRecords: leaderLines (what "current leaders" pre-fetches into the prompt)');
  show(leaderLines(SERVER, { now: NOW }));

  if (name === 'populated') {
    sub('valheimRecords: simulate a leader change on connected_seconds_alltime and render the #barrkbot post');
    const previous = { standings: st.map((s) => s.field === 'connected_seconds_alltime' ? { ...s, holder: 'Cpt JD', value: 38810 } : s), announcedAt: {} };
    const { changes, declined } = detectChanges(previous, st, { now: NOW });
    for (const c of changes) { console.log('  HEADLINE:', headlineFor(c)); console.log('  FACTS to the local model:', JSON.stringify(recordFacts(c))); }
    console.log('  declined:', JSON.stringify(declined));
    sub('valheimRecords: simulate a leader change on deaths_alltime (declared not-achievement)');
    const prev2 = { standings: st.map((s) => s.field === 'deaths_alltime' ? { ...s, holder: 'Rohan', value: 1 } : s), announcedAt: {} };
    const r2 = detectChanges(prev2, st, { now: NOW });
    console.log('  changes:', r2.changes.map(headlineFor), ' declined:', JSON.stringify(r2.declined.filter((d) => d.id.includes('deaths'))));

    sub('STALENESS: same file read 61 minutes after generated_at (server stopped, nothing rewrote it)');
    const later = new Date(Date.parse(data.generated_at) + 61 * 60_000);
    const stale = readModExport(SERVER, { mod: 'Wonderland', now: later });
    console.log('ageMinutes:', stale.exports[0].ageMinutes, '| stale note:', stale.exports[0].notes.find((n) => /minutes old/.test(n)));
    console.log('server block still says:', JSON.stringify({ online: stale.exports[0].collections.server.values.online, players_online: stale.exports[0].collections.server.values.players_online, players_online_names: stale.exports[0].collections.server.values.players_online_names }));
    console.log('per-player online_now still says:', JSON.stringify(Object.fromEntries(stale.exports[0].collections.players.rows.map((r) => [r.name, r.online_now]))));
    sub('STALENESS: 30 minutes after a clean shutdown (below STALE_AFTER_MINUTES=' + STALE_AFTER_MINUTES + ') -> no caveat at all');
    const soon = new Date(Date.parse(data.generated_at) + 30 * 60_000);
    const s30 = readModExport(SERVER, { mod: 'Wonderland', now: soon });
    console.log('ageMinutes:', s30.exports[0].ageMinutes, '| any staleness note?', s30.exports[0].notes.some((n) => /minutes old/.test(n)));
    console.log('two-time-bases note says of server:', s30.exports[0].notes.find((n) => /TWO TIME BASES/.test(n))?.slice(-220));

    sub('SHUTDOWN: the file the mod writes from Plugin.OnDestroy (online=false, nobody on), read 30 minutes later');
    const stopped = JSON.parse(JSON.stringify(data));
    stopped.server.online = false; stopped.server.players_online = 0; stopped.server.players_online_names = [];
    for (const r of Object.values(stopped.players)) r.online_now = false;
    writeFileSync(join(serverDir, 'BepInEx', 'config', 'Wonderland', 'barrkbot_wonderland.json'), JSON.stringify(stopped, null, 2));
    await syncModExports(SERVER, { force: true });
    const off = readModExport(SERVER, { mod: 'Wonderland', now: soon });
    console.log('server block now says:', JSON.stringify({ online: off.exports[0].collections.server.values.online, players_online: off.exports[0].collections.server.values.players_online }));
    console.log('per-player online_now:', JSON.stringify(Object.fromEntries(off.exports[0].collections.players.rows.map((r) => [r.name, r.online_now]))));
  }
}
} finally {
  rmSync(box, { recursive: true, force: true });
}
console.log('\n(done)');
