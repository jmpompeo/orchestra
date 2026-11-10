"""Execute the pre-registered serial schedule; never tune/drop/retry failed trials."""
import argparse
import asyncio
from pathlib import Path
import hashlib
import json
import os
import subprocess
import uuid
import corpus
import measurement
import runner

HERE=Path(__file__).resolve().parent
ROOT=HERE.parent
OUTPUT=ROOT/'experiment-v2'
PROTOCOL=ROOT/'protocol.json'


def fingerprint():
    settings=json.loads((HERE/'settings.json').read_text())
    files={str(p.relative_to(HERE)):runner.sha(p) for p in sorted(HERE.iterdir()) if p.is_file()}
    source={str(p.relative_to(corpus.SOURCE)):runner.sha(p) for p in sorted(corpus.SOURCE.rglob('*')) if p.is_file()}
    return {'harness':files,'source':source,'protocol_sha256':runner.sha(PROTOCOL)}


def get_manifest(args):
    m=runner.make_manifest(args.baseline,args.candidate,corpus,3,False,args.codex,args.timeout,args.grading_timeout)
    m['five_task_protocol']=fingerprint()
    return m


def export_packet(result):
    run=OUTPUT/'runs'/result['trial_id'];work=run/'workspace'
    folder=OUTPUT/'packets';folder.mkdir(exist_ok=True)
    mapfile=OUTPUT/'packet-map.json';mapping=json.loads(mapfile.read_text()) if mapfile.exists() else {}
    if result['trial_id'] in mapping.values():return
    opaque=uuid.uuid4().hex[:10];mapping[opaque]=result['trial_id']
    diff=runner.git(work,'diff','--no-ext-diff','HEAD','--','src/AgentHarness','tests/AgentHarness.Tests')
    new=runner.git(work,'ls-files','--others','--exclude-standard','-z').split('\0')
    for name in sorted(n for n in new if n.endswith('.cs')):
        p=work/name
        if p.is_file() and not p.is_symlink():diff+='\nNEW FILE '+name+'\n'+p.read_text()
    (folder/(opaque+'.txt')).write_text('TASK\n'+corpus.CASES[result['case']]['prompt']+'\n\nPATCH\n'+diff)
    runner.dump(mapfile,mapping)


def summarize():
    reviews_file=OUTPUT/'reviews.json';reviews=json.loads(reviews_file.read_text()) if reviews_file.exists() else {}
    results=runner.read_results(OUTPUT);adjusted=[];missing=[];failed=[]
    for row in results:
        r=dict(row);review=reviews.get(row['trial_id'],{})
        contract=review.get('contract_pass')
        if type(contract) is not bool:missing.append(row['trial_id'])
        elif not contract:
            failed.append(row['trial_id']);r['accepted']=False
            r['grade']=dict(row.get('grade',{}),accepted=False,critical_pass=False)
            r['grade']['functional_groups']=dict(r['grade'].get('functional_groups',{}),manual_contract=False)
        adjusted.append(r)
    result=measurement.evaluate(adjusted,reviews,corpus.CASES)
    result['manual_contract_missing']=missing;result['manual_contract_failures']=failed
    result['gates']['manual_contracts_complete']=bool(results) and not missing
    result['gates']['manual_contracts_pass']=bool(results) and not failed
    result['confirmed']=all(result['gates'].values())
    if missing:result['status']='blocked'
    elif failed:result['status']='not_confirmed'
    result['scope']='Five real Orchestra C# CLI tasks, three paired repetitions under frozen conditions; no universal productivity claim.'
    result['manual_acceptance_adjustments']='Raw results remain immutable; any manually identified contract failure is scored as an unaccepted attempt in this analysis.'
    runner.dump(OUTPUT/'summary.json',result)
    return result


async def execute(args,manifest):
    protocol=json.loads(PROTOCOL.read_text())
    for pair in protocol['schedule']:
        for arm in pair['order']:
            if (OUTPUT/'manual-stop.json').exists():return
            case=pair['case'];rep=pair['repetition'];tid=f'{case}-r{rep}-{arm}-a1'
            old=runner.read_results(OUTPUT)
            completed=[r for r in old if r['trial_id']==tid]
            if completed:
                if len(completed)!=1 or completed[0].get('errors') or not completed[0].get('complete'):
                    raise RuntimeError('Incomplete planned attempt retained; no automatic retries')
                continue
            if (OUTPUT/'stop.json').exists():return
            runner.freeze_manifest(OUTPUT/'manifest.json',get_manifest(args))
            result=await runner.trial(args,corpus,arm,case,rep,1,manifest)
            export_packet(result)
            print(json.dumps({k:result.get(k) for k in ['trial_id','accepted','elapsed_seconds','total_tokens','errors','grade']}),flush=True)
            summarize()
            if result['errors'] or (arm=='candidate' and not result['accepted']):
                runner.dump(OUTPUT/'stop.json',{'trial':tid,'reason':'Infrastructure/native evidence failure or candidate correctness/acceptance failure. Preserve all evidence and revisit without tuning this experiment.'});return


def main():
    p=argparse.ArgumentParser();p.add_argument('--run',action='store_true');p.add_argument('--summarize',action='store_true');a=p.parse_args()
    if a.summarize:
        print(json.dumps(summarize(),indent=2));return
    protocol=json.loads(PROTOCOL.read_text())
    args=argparse.Namespace(baseline=Path(protocol['baseline']),candidate=Path(protocol['candidate']),output=OUTPUT,codex=protocol['codex'],timeout=protocol['agent_timeout_seconds'],grading_timeout=protocol['grading_timeout_seconds'])
    manifest=get_manifest(args)
    if not a.run:print(json.dumps({'plan':protocol,'manifest':manifest},indent=2));return
    auth=Path(os.environ.get('CODEX_HOME',str(Path.home()/'.codex')))/'auth.json'
    args.auth_source=auth.resolve()
    if not args.auth_source.is_file():raise RuntimeError('Native auth missing')
    OUTPUT.mkdir(mode=0o700,exist_ok=True);runner.freeze_manifest(OUTPUT/'manifest.json',manifest)
    asyncio.run(execute(args,manifest));summarize()

if __name__=='__main__':main()
