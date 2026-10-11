"""Exercise the packaged Manager -> standalone converter bridge; tiny CPU fixtures only."""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.request
import uuid

parser=argparse.ArgumentParser()
parser.add_argument('--package',type=Path,required=True)
parser.add_argument('--converter-source',type=Path,required=True)
args=parser.parse_args()
sys.path[:0]=[str(args.converter_source/'tests'),str(args.converter_source/'vendor')]
from fixtures import checkpoint

base=Path(__file__).resolve().parents[1]/'artifacts'
base.mkdir(exist_ok=True)
passed=[]
def check(value,message):
    if not value:raise AssertionError(message)
    passed.append(message)
    print('PASS '+message,flush=True)
def port():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
ports=set()
while len(ports)<3:ports.add(port())
control,public,internal=ports
token=uuid.uuid4().hex+uuid.uuid4().hex
http=urllib.request.build_opener(urllib.request.ProxyHandler({}))
def request(route,payload=None):
    data=json.dumps(payload).encode() if payload is not None else None
    req=urllib.request.Request(f'http://127.0.0.1:{control}/control/v1/'+route,data=data,headers={'Content-Type':'application/json','X-NInferEZ-Control-Token':token})
    with http.open(req,timeout=100) as response:return json.load(response)
def wait():
    end=time.monotonic()+40
    while time.monotonic()<end:
        status=request('converter/status')
        if not status['running']:return status
        time.sleep(.1)
    raise AssertionError('Small CPU fixture job exceeded 40 seconds')

with tempfile.TemporaryDirectory(prefix='converter-smoke-',dir=base) as tmp:
    root=Path(tmp);(root/'Data').mkdir()
    (root/'Data'/'settings.json').write_text(json.dumps({'publicPort':public,'backendPort':internal,'lockPublicPort':True,'autoCheckCatalog':False,'autoCheckEngines':False,'autoCheckUpdates':False}),encoding='utf-8')
    env=dict(os.environ,NINFEREZ_MANAGER_CONTROL_PORT=str(control),NINFEREZ_MANAGER_CONTROL_TOKEN=token,
        NINFEREZ_MANAGER_DATA_ROOT=str(root/'Data'),NINFEREZ_MANAGER_MODELS_ROOT=str(root/'Models'),NINFEREZ_MANAGER_ENGINE_ROOT=str(root/'UnusedEngine'))
    log=(root/'backend.log').open('w',encoding='utf-8')
    child=subprocess.Popen([str(args.package/'Backend'/'NInferManager.Backend.exe')],cwd=root,env=env,stdout=log,stderr=log,creationflags=0x08000000)
    try:
        deadline=time.monotonic()+25
        while True:
            try:request('health');break
            except Exception:
                if child.poll() is not None or time.monotonic()>deadline:raise
                time.sleep(.1)
        check(request('converter/status')['phase']=='Ready','converter starts idle in packaged Manager')
        caps=request('converter/capabilities')
        check('auto' in caps['profiles'] and caps['devices']==['cpu'],'packaged CPU runtime reports real profiles without requiring Python installation')
        bad=root/'missing-config.gguf';bad.write_bytes(b'GGUF')
        request('converter/check',{'source':str(bad),'output':str(root/'bad.ninfer')})
        failed=wait()
        check(failed['phase']=='Failed' and 'incomplete' in failed['message'].lower(),'truncated GGUF yields an actionable file error rather than an internal parser exception')
        source,_,_=checkpoint(root/'tiny-source')
        output=root/'tiny-result.ninfer'
        value={'source':str(source),'output':str(output),'profile':'auto','device':'cpu','rowsPerChunk':7}
        request('converter/probe',{**value,'output':'','configFile':str(root/'missing-config.json')})
        missing=wait()
        discovery=json.loads(missing['plan'])
        check(missing['phase']=='Scanned' and not discovery['ready'] and any(r['key']=='configFile' and r['status']=='missing' for r in discovery['requirements']),
              'Wizard returns attachable missing configuration requirements without starting conversion')
        request('converter/analyze',{**value,'output':''})
        analysis=wait()
        options=json.loads(analysis['plan'])['options']
        check(analysis['phase']=='Options ready' and not analysis['canConvert'] and len(options)>1,
              'Wizard compares actual conversion alternatives without selecting or authorizing a profile')
        check(any(o['profile']=='q4' and o['supported'] and o['plan']['encodedBytes']>0 for o in options),
              'Wizard exposes a real supported Q4 plan and its encoded weight estimate')
        manual={**value,'output':str(root/'manual.ninfer'),'profile':'q4','device':'auto',
                'tensorOverrides':[{'selector':'text/layers/*/mlp/down','format':'bf16','method':'cast_direct','layout':'auto','activationPolicy':'','parameters':{}}],
                'resourceFolder':str(source),'shardOverrides':{},'validateTimeoutSeconds':120}
        request('converter/check',manual)
        manual_plan=wait()
        check(manual_plan['canConvert'] and any('mlp/down' in ','.join(t['parameters']) and t['format']=='bf16' for t in json.loads(manual_plan['plan'])['tensorPlan']),
              'Manual per-weight assignments reach the existing planner without a second conversion implementation')
        request('converter/convert',manual)
        manual_result=wait()
        check(manual_result['phase']=='Complete' and Path(manual['output']).is_file(),
              'Manual settings complete a real tiny CPU conversion through the packaged Manager')
        request('converter/check',value)
        status=wait()
        check(status['phase']=='Compatible' and status['canConvert'],'real tiny checkpoint compatibility succeeds through independent backend')
        check(json.loads(status['plan'])['encodedBytes']>0,'real conversion plan reports encoded bytes')
        check(bool(json.loads(status['plan'])['fileState']),'Final checked plan exposes the captured file state for the Wizard review guard')
        request('converter/convert',value)
        status=wait()
        check(status['phase']=='Complete' and output.is_file() and Path(str(output)+'.conversion.json').is_file(),'Manager bridge completes and atomically publishes real fixture output/report')
        check(status['percent']==100 and not status['runtimeVerified'],'real byte progress completes without claiming GPU verification')
        report=json.loads(Path(str(output)+'.conversion.json').read_text(encoding='utf-8'))
        check(report['nativeInspected'],'bundled host-only NInfer inspector validates generated artifact')
        repack={'source':str(output),'output':str(root/'repacked.ninfer'),'profile':'preserve'}
        request('converter/analyze',repack)
        repack_options=json.loads(wait()['plan'])['options']
        check(len(repack_options)==1 and repack_options[0]['profile']=='preserve' and repack_options[0]['plan']['mode']=='repack',
              'Existing NInfer artifact exposes only the lossless repack route in Wizard')
        request('converter/check',repack)
        check(wait()['canConvert'],'Repack final plan can be checked after Wizard selection')
        changed=output.read_bytes()+b'\0'
        output.write_bytes(changed)
        request('converter/convert',repack)
        rejected=wait()
        check(rejected['phase']=='Failed' and 'changed' in rejected['message'].lower() and not Path(repack['output']).exists(),
              'Changed source after review is rejected by existing file-state guard without publishing output')
        output.write_bytes(changed[:-1])
        linked=request('models/link',{'path':str(output)})
        check(linked['success'],'converted artifact links into Manager library without weight copying or GPU load')
        models=request('models')
        check(any(m['installed'] and m['fileName']==output.name for m in models),'linked converted artifact is visible in library')
        check(request('status')['engine']=='Unloaded','converter integration never loaded the inference engine')
    finally:
        child.terminate()
        try:child.wait(timeout=5)
        except subprocess.TimeoutExpired:child.kill();child.wait()
        log.close()
report={'passedChecks':len(passed),'checks':passed,'gpuTests':False,'nativeUiTests':False,'largeModelConversion':False}
(base/'converter-wizard-smoke-results.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report),flush=True)
