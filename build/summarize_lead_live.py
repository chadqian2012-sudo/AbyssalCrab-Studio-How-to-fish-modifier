"""Preserve a truthful feature observation alongside the interrupted transport test."""
from pathlib import Path
import json, hashlib
root=Path(__file__).resolve().parents[1]
def read(p):return json.loads((root/p).read_text(encoding='utf-8-sig'))
commands=read('verification/lead-live-commands.json')
sessions=[c['Applied'] for c in commands if c['id']=='session.inspect' and c['succeeded']]
waves=[c['Applied'] for c in commands if c['id']=='fun.event.execute' and c['succeeded']]
assert len(waves)>=4 and sum(w['registered'] for w in waves[:3])==24
assert all(w['registered']==8 for w in waves)
diagnostics=[s['predictiveAim'] for s in sessions]
assert max(d['headFrames'] for d in diagnostics)>0
assert max(d['predictedFrames'] for d in diagnostics)>0
assert max(d['silentShots'] for d in diagnostics)>0
first_ids={w['waveId'] for w in waves[:3]}
assert any(sum(w['alive'] for w in d.get('seagullWaves',[]) if w['waveId'] in first_ids)==0 for d in diagnostics)
reconnect=read('verification/lead-reconnect-results.json')
assert reconnect['passed'] and reconnect['cleanup_verified'] and reconnect['stable_heartbeat_samples']>=120
for c in reconnect['commands']:
    assert c['Succeeded']
assert not reconnect['final']['runtimeHooks']['nativeAimEligible']
assert not reconnect['final']['runtimeHooks']['nativeAimTrigger']
observed_main=root/'verification/bin-net8/HowToFishTrainer.dll'
runtime=root/'patched/HowToFishTrainer.Runtime.dll'
assert runtime.read_bytes() in observed_main.read_bytes()
report={
 'feature_observation_verified':True,'long_test_interrupted':True,'interruption':'Pipe is broken at approximately 86 seconds; game stayed active; user reports no competing window or world exit',
 'initial_seagulls':sum(w['registered'] for w in waves[:3]),'refill_waves':len(waves)-3,'total_registered':sum(w['registered'] for w in waves),
 'refill_after_initial_alive_zero_verified':True,'head_tracking_frames':max(d['headFrames'] for d in diagnostics),
 'prediction_frames':max(d['predictedFrames'] for d in diagnostics),'silent_projectile_redirections':max(d['silentShots'] for d in diagnostics),
 'weapons':list({json.dumps(d['weapon'],sort_keys=True):{k:d['weapon'][k] for k in ('name','speed','gravity','hitscan')} for d in diagnostics if 'weapon'in d}.values()),
 'reconnect_and_cleanup_verified':True,'stable_heartbeat_samples':reconnect['stable_heartbeat_samples'],
 'observed_main_sha256':hashlib.sha256(observed_main.read_bytes()).hexdigest(),'observed_embedded_runtime_matches_final':True,
 'runtime_sha256':hashlib.sha256(runtime.read_bytes()).hexdigest(),'all_weapon_hit_rates_measured':False,
 'automatic_firing':False,'save_invoked':False,'user_mouse_keyboard_operated':False}
# Deduplicate weapon definitions while excluding muzzle movement.
report['weapons']=list({json.dumps(w,sort_keys=True):w for w in report['weapons']}.values())
(root/'verification/lead-live-observation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
previous=root/'verification/lead-live-results.json'
saved=root/'history/lead-before-20261017-064107/preliminary-live-failure.json'
if previous.exists() and not saved.exists():saved.write_bytes(previous.read_bytes())
previous.write_text(json.dumps({'passed':False,'long_test_interrupted':True,
    'failure':'Pipe is broken after approximately 86 seconds; subsequent separate 120-sample reconnection test passed',
    'feature_observation':report,'raw_commands':'lead-live-commands.json','raw_console':'lead-live-console.log',
    'recovery_report':'lead-reconnect-results.json'},ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
