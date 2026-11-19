#!/usr/bin/env node
// Process-boundary test double. This does not test the model's business understanding.
const fs=require('fs'),path=require('path'),args=process.argv.slice(2);
if(args.includes('--help')){console.log('--prompt --silent --deny-tool --available-tools --disable-builtin-mcps --no-custom-instructions --no-auto-update --stream --no-color --no-ask-user --output-format --attachment piped stdin');process.exit(0);}
if(args.includes('--version')){console.log('business-test-fixture');process.exit(0);}
if(args.includes('--allow-all')||args.includes('--allow-tool')||!args.includes('--available-tools')||!args.includes('--disable-builtin-mcps')||args.includes('--prompt'))process.exit(8);
const prompt=fs.readFileSync(0,'utf8'),r=JSON.parse(prompt.split('UNTRUSTED_DATA\n')[1]);
let reply,phase;
if(r.sessionId){
 phase='frame';const id=r.turns.filter(t=>t.role==='user').at(-1).id;
 reply={understanding:'Comprendre les règles de consultation des contrats.',question:'',questionReason:'',ready:true,brief:{name:r.current?.name||'Contrats',objective:'Comprendre les règles métier',scope:'Consultation des contrats',items:[{topic:'objective',text:'Comprendre les règles métier',origin:'declared',turnIds:[id]},{topic:'scope',text:'Consultation des contrats',origin:'declared',turnIds:[id]}],openQuestions:['Qui approuve la cible ?']}};
}else if(r.protocol==='business-document-v1'){
 phase='document';
 const all=r.journal.concat(r.visible.map(v=>v.unit)),seen=new Set(all.map(u=>u.id));
 const next=r.sections.flatMap(s=>Array.from({length:s.count},(_,i)=>s.start+i)).find(n=>!seen.has('e'+n));
 reply={action:next?'read':'finish',summary:'La consultation des contrats comporte des conditions à confirmer.',reads:next?[{start:next,count:500}]:[],findings:all.length?[{entity:'Consultation des contrats',kind:'process',statement:'La consultation nécessite de vérifier les règles de visibilité.',contribution:'new',evidenceIds:[all[0].id],relatedClaimIds:[],uncertain:true,qualifiedName:null,conditions:[],aliases:['accès contrat']}]:[],relations:[],coverage:r.sections.filter(s=>Array.from({length:s.count},(_,i)=>'e'+(s.start+i)).every(id=>seen.has(id))).map(s=>({sectionId:s.id,status:'interpreted',reason:'Lecture simulée complète de cette section.'})),questions:['Qui confirme les règles de visibilité ?'],visualStatus:'not-applicable'};
 if(fs.existsSync(path.join(__dirname,'bad-proof-once'))&&all.length){fs.unlinkSync(path.join(__dirname,'bad-proof-once'));reply.findings[0].evidenceIds=['unknown'];}
}else{
 phase='qualify';reply={purpose:'Étude de la consultation des contrats',topics:['Contrats'],role:'unknown',status:'proposal',question:'',reason:'Rôle mixte à préciser.'};
}
fs.appendFileSync(path.join(__dirname,'business-calls.jsonl'),JSON.stringify({phase,retry:prompt.startsWith('BUSINESS_CONTRACT RETRY:'),characters:prompt.length})+'\n');
console.log(JSON.stringify({type:'assistant.message',data:{content:JSON.stringify(reply)}}));
console.log(JSON.stringify({type:'result',exitCode:0}));
