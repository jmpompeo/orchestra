"""Focused offline C# calibration. No model calls or historical-result writes."""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

HERE=Path(__file__).resolve().parent


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--source',type=Path,required=True,help='Frozen independent Orchestra source snapshot')
    p.add_argument('--candidate',type=Path,help='Optional saved candidate workspace, copied before testing')
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--harness',type=Path,default=HERE.parent/'harness')
    p.add_argument('--project',type=Path,default=HERE.parent/'results/repro/Repro.csproj')
    a=p.parse_args();a.source=a.source.resolve();a.output=a.output.resolve()
    if a.output.exists():raise SystemExit('Use a fresh output directory; do not overwrite evidence')
    inputs=[a.source]+([a.candidate.resolve()] if a.candidate else [])
    if any(a.output==source or source in a.output.parents for source in inputs):raise SystemExit('Keep evidence outside source and candidate inputs')
    a.output.mkdir(parents=True)
    checks={};built={};expected_failures={};case=json.loads((HERE/'status-json.json').read_text())
    with tempfile.TemporaryDirectory(prefix='boundary-check-') as temp:
        scratch=Path(temp);h=scratch/'harness';h.mkdir()
        definitions=json.loads((a.harness/'cases.json').read_text());definitions['status-json']=case
        (h/'cases.json').write_text(json.dumps(definitions));(h/'settings.json').write_text(json.dumps({'source':str(a.source)}))
        shutil.copy2(a.harness/'corpus.py',h/'corpus.py')
        spec=importlib.util.spec_from_file_location('boundary_corpus',h/'corpus.py');corpus=importlib.util.module_from_spec(spec);spec.loader.exec_module(corpus)
        variants={'reference':None,'old-reference':'consumed-json-value-v1','permissive-tools-value':'permissive-tools-value'}
        if a.candidate:variants['saved-candidate']='saved-candidate'
        for name,mutant in variants.items():
            folder=scratch/name;folder.mkdir();app=folder/'subject'
            if mutant=='saved-candidate':corpus.copy_submission(a.candidate.resolve(),app)
            else:
                shutil.copytree(a.source,app);corpus.install_reference('status-json',app,mutant)
            project=folder/'checks';project.mkdir();shutil.copy2(a.project,project/'Checks.csproj')
            (project/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>\n')
            (project/'Program.cs').write_text(corpus.heldout_program('status-json'))
            command=['dotnet','build',str(project/'Checks.csproj'),'-c','Release','--disable-build-servers','-m:1','-p:SubjectRoot='+str(app)]
            built[name]=corpus.command(command,folder,a.output/(name+'-build.log'))==0
            if not built[name]:raise AssertionError('Compilation failure is not a behavioral rejection: '+name)
            groups=list(case['functional_groups']) if name=='reference' else ['argument-boundaries']
            for group in groups:
                rc=corpus.command(['dotnet',str(project/'bin/Release/net10.0/Checks.dll'),group],folder,a.output/(name+'-'+group+'.log'))
                if name=='reference':checks[group]=rc==0
                else:expected_failures[name]=rc!=0 and 'CHECK:' in (a.output/(name+'-'+group+'.log')).read_text()
            if name=='reference':
                checks['existing-dotnet-suite']=corpus.command(corpus.CHECK,app,a.output/'existing-dotnet-suite.log')==0
        result={'schemaVersion':1,'model_calls':0,'reference_checks':checks,'compiled_variants':built,'expected_behavioral_rejections':expected_failures,
                'passed':all(checks.values()) and all(built.values()) and all(expected_failures.values()),
                'limits':['Focused deterministic calibration only; no benchmark restart or performance claim.','Existing three status groups and suite checked; only new boundary faults and optional saved candidate rechecked.','Frozen v1 experiment and its reference are unchanged.']}
        (a.output/'summary.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))
        if not result['passed']:raise SystemExit(1)

if __name__=='__main__':main()
