import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { initializeApp } from "firebase-admin/app";
import { passClaim, passProgress, validatePass, resolveEventPass } from "../lib/event-pass.js";
import { readProduct } from "../lib/products.js";
import { revokeReceipt } from "../lib/refunds.js";

initializeApp({ projectId: "event-pass-unit-tests" });
const manifest = JSON.parse(readFileSync(new URL("../../../Assets/StreamingAssets/Content/manifest.json", import.meta.url)));
const event = manifest.events.find(e => e.id === "first_bloom");
const store = JSON.parse(readFileSync(new URL("../../../Assets/StreamingAssets/Content/progression.json", import.meta.url))).store;
const product = store.products.find(p => p.id === event.premiumProductId);
assert.equal(event.milestones.length, 40);
assert.equal(new Set(event.levels).size, 40);
assert.equal(product.eventPassId, event.id);
assert.equal(product.referenceUsdCents, 499);
assert.equal(readProduct({ [product.id]: product }, product.id).eventPassId, event.id);
for (const patch of [{kind:"consumable"}, {credits:1}, {gems:1}, {capacity:10}, {eventPassId:"../wallet"}])
  assert.throws(() => readProduct({ [product.id]: {...product,...patch} }, product.id));
validatePass(event);
assert.throws(() => validatePass({...event, milestones:[...event.milestones,...event.milestones]}));
assert.throws(() => validatePass({...event, milestones:[{goal:1,premiumGems:-1}]}));
assert.throws(() => validatePass({...event, levels:[...event.levels,event.levels[0]]}));
const sum = key => event.milestones.reduce((n,t) => n+(t[key]??0),0);
assert.deepEqual([sum("credits"),sum("premiumCredits"),sum("premiumGems")],[6320,12000,600]);
for (const [goal,credits] of [[1,60],[2,90],[4,250],[6,400],[8,600],[10,1000]])
  assert.equal(event.milestones.find(t=>t.goal===goal).credits,credits);
const config = {seeds:{credits:1250,gems:0}, rewards:{creditsFirstClear:0,creditsPerStar:0},
  levelChapters:Object.fromEntries(event.levels.map(id=>[id,"chapter"])), chapterRewards:{}, events:[event]};
const levels = Object.fromEntries(event.levels.map(id=>[id,{stars:3,firstClearedUnix:event.startUnix+1}]));
const owned = {owned:true, productId:product.id};
assert.equal(passProgress(event,levels,config),40);
assert.equal(passProgress(event,{[event.levels[0]]:{stars:3,firstClearedUnix:event.endUnix}},config),0);
assert.equal(passProgress(event,{[event.levels[0]]:{stars:NaN,firstClearedUnix:event.startUnix}},config),0);
assert.equal(passProgress(event,levels,{...config,levelChapters:{}}),0);
assert.equal(passClaim(event,{},40,40).credits,0);
assert.equal(passClaim(event,{...owned,productId:"different"},40,40).credits,0);
assert.equal(passClaim(event,owned,0,40).credits,0);
assert.equal(passClaim(event,owned,40,NaN).credits,0);
let state={...owned}, coins=0,gems=0;
for(let goal=1;goal<=40;goal++) {
  const grant=passClaim(event,state,goal,40);
  state.collectedGoal=grant.through; coins+=grant.credits;gems+=grant.gems;
  assert.equal(passClaim(event,state,goal,40).credits,0);
}
assert.deepEqual([coins,gems],[12000,600]);
assert.deepEqual(passClaim(event,owned,40,40),{through:40,credits:12000,gems:600});

// Strict transaction double: atomic staged writes, serial retries, no reads after writes.
const docs = new Map([["config/progression",config],["players/test",{levels}],
  ["players/test/eventPasses/first_bloom",{...owned,definition:event}]]);
let tail=Promise.resolve();
const db={doc:path=>({path}),runTransaction:fn=>{
  const run=tail.then(async()=>{
    let written=false;
    const staged=new Map(docs);
    const get=async ref=>{assert.equal(written,false,"all reads precede writes");return {exists:staged.has(ref.path),data:()=>staged.get(ref.path)};};
    const result=await fn({get,getAll:async(...refs)=>Promise.all(refs.map(get)),update:(ref,patch)=>{
      written=true; const value=structuredClone(staged.get(ref.path)); assert.ok(value);
      for(const [key,item] of Object.entries(patch)) {
        const parts=key.split("."); if(parts.length===2) value[parts[0]][parts[1]]=item; else value[key]=item;
      } staged.set(ref.path,value);
    },set:(ref,value,options)=>{
      written=true; staged.set(ref.path,options?.merge?{...staged.get(ref.path),...value}:value);
    }});
    docs.clear();for(const [key,value] of staged)docs.set(key,value);return result;
  });tail=run.catch(()=>{});return run;
}};
const first=await resolveEventPass(db,"test",event.id,0);
assert.equal(first.owned,true);assert.equal(first.collectedGoal,0);
const [a,b]=await Promise.all([resolveEventPass(db,"test",event.id,40),resolveEventPass(db,"test",event.id,40)]);
assert.equal(a.credits+b.credits,12000);assert.equal(a.gems+b.gems,600);
assert.equal(docs.get("players/test/private/wallet").credits.granted,13250);
assert.equal((await resolveEventPass(db,"test",event.id,40)).credits,0,"lost reply retry is idempotent");
assert.equal((await resolveEventPass(db,"test",event.id,1)).collectedGoal,40,"stale device cannot lower floor");
docs.set("config/progression",{...config,events:[]});
assert.equal((await resolveEventPass(db,"test",event.id,0)).owned,true,"sold contract survives catalog retirement");
docs.set("receipts/google__purchase",{uid:"test",productId:product.id,eventPassId:event.id,granted:{},revokedAt:null});
docs.set("players/test/eventPasses/first_bloom",{...docs.get("players/test/eventPasses/first_bloom"),receiptPath:"receipts/google__purchase"});
assert.equal(await revokeReceipt("google","purchase","unit-test",db),true);
assert.equal(await revokeReceipt("google","purchase","retry",db),false);
await assert.rejects(resolveEventPass(db,"test",event.id,40),/verified pass purchase/);
assert.equal((await resolveEventPass(db,"test",event.id,0)).owned,false);
assert.equal(docs.get("players/test/private/wallet").credits.granted,1250,"refund reverses exactly the premium coins");
assert.equal(docs.get("players/test/private/wallet").gems.granted,0,"refund reverses premium gems once");
console.log("Event pass: catalog, balance, gates, retries, concurrent claims, saved contract and revocation checks passed.");
