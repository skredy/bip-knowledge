#!/usr/bin/env node
// Test double only. No network/model calls. Records requests in its own test directory.
const fs=require('fs'), path=require('path');
const args=process.argv.slice(2);
const stdinEnabled=fs.existsSync(path.join(__dirname,'stdin-enabled'));
if(args.includes('--help')){console.log('--prompt --silent --deny-tool --available-tools --disable-builtin-mcps --no-custom-instructions --no-auto-update --stream --no-color --no-ask-user --output-format --attachment'+(stdinEnabled?' piped stdin':''));process.exit(0);}
if(args.includes('--version')){console.log('guided-test-fixture');process.exit(0);}
if(args.includes('--allow-all')||!args.includes('--available-tools')||args[args.indexOf('--output-format')+1]!=='json')process.exit(8);
const prompt=stdinEnabled?fs.readFileSync(0,'utf8'):args[args.indexOf('--prompt')+1];
if(stdinEnabled&&args.includes('--prompt'))process.exit(11);
const request=JSON.parse(prompt.split('UNTRUSTED_DATA\n')[1]);
if(request.protocol!=='guided-v1')process.exit(9);
const image=['.png','.jpg','.jpeg'].includes(request.format);
if(image&&request.visible.length&&(!args.includes('--attachment')||!fs.existsSync(args[args.indexOf('--attachment')+1])))process.exit(10);
fs.appendFileSync(path.join(__dirname,'guided-calls.jsonl'),JSON.stringify({step:request.step,format:request.format,visible:request.visible.length,ids:request.visible.map(v=>v.unit.id),promptCharacters:prompt.length,stdin:stdinEnabled})+'\n');
const seen=new Set(request.visible.map(v=>v.number));
let next=0;
for(const range of request.pending){for(let n=range.start;n<range.start+range.count;n++){if(!seen.has(n)){next=n;break;}}if(next)break;}
const reply={action:next?'read':'finish',plan:'Comprendre les sections avant de conclure.',summary:next?'':'Synthèse simulée, à confirmer sur le corpus réel.',reads:next?[{start:next,count:Math.min(200,request.totalUnits-next+1)}]:[],query:'',offset:0,
 findings:request.visible.map(v=>({entity:'Commande',kind:'rule',statement:v.unit.text,contribution:'new',evidenceIds:[v.unit.id],relatedClaimIds:[],uncertain:false,qualifiedName:null,conditions:[]})),
 reviews:request.visible.map(v=>({unitId:v.unit.id,status:'analyzed',reason:''})),questions:[],visualStatus:image?'observed':'not-applicable'};
if(fs.existsSync(path.join(__dirname,'reject-next'))&&request.visible.length){fs.unlinkSync(path.join(__dirname,'reject-next'));reply.findings[0].evidenceIds=['unknown-proof'];}
console.log(JSON.stringify({type:'assistant.message',data:{content:JSON.stringify(reply)}}));
console.log(JSON.stringify({type:'result',exitCode:0}));
