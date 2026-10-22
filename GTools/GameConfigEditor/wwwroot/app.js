'use strict';
const $=id=>document.getElementById(id);
const labels={id:'配置 ID',modelId:'模型资源 ID',cycleTime:'射击间隔（秒）',range:'射程（米）',damage:'伤害',rangeModifier:'距离伤害衰减系数',automatic:'自动射击',silenced:'消音',pelletCount:'弹丸数量',spreadAngle:'散射角度',magazineSize:'弹匣容量',reserveAmmo:'备用弹药',reloadDuration:'换弹时间（秒）',version:'配置版本',respawnDelay:'重生延迟（秒）',infiniteAmmoReserve:'无限备弹',infiniteThrowables:'无限投掷物',autoRechargeTaser:'电击枪自动充能',components:'玩法组件'};
let token,name,revision,documentData,index=0,dirty=false;
async function request(url,options){const response=await fetch(url,options);const data=await response.json();if(!response.ok)throw Error(data.error||'操作失败：'+response.status);return data;}
function status(message,error=false){$('status').textContent=message;$('status').className=error?'error':'';}
function showJSON(){ $('json').textContent=JSON.stringify(documentData,null,2); }
function showItems(){ $('items').replaceChildren(); if(!documentData.weapons)return;documentData.weapons.forEach((item,i)=>{if(!item.id.includes($('search').value))return;const b=document.createElement('button');b.textContent=item.id;b.className=i===index?'selected':'';b.onclick=()=>{index=i;showItems();render();};$('items').append(b);}); }
function render(){
 $('path').textContent='GConfig / '+name;const item=documentData.weapons?documentData.weapons[index]:documentData;
 $('title').textContent=item.id||'武器配置';$('fields').replaceChildren();
 const values={...item};if(documentData.weapons){Object.assign(values,{pelletCount:1,spreadAngle:0,magazineSize:30,reserveAmmo:90,reloadDuration:2.5,automatic:true,silenced:false},item);}
 for(const [key,value] of Object.entries(values)){
  const box=document.createElement('div'),label=document.createElement('label'),input=document.createElement('input');
  label.textContent=labels[key]||key;input.id='field-'+key;label.htmlFor=input.id;
  input.type=typeof value==='boolean'?'checkbox':typeof value==='number'?'number':'text';input.step='any';
  if(typeof value==='boolean')input.checked=value;else input.value=Array.isArray(value)?value.join(', '):value;
  if(key==='id'||key==='version')input.readOnly=true;
  input.oninput=()=>{item[key]=typeof value==='boolean'?input.checked:typeof value==='number'?Number(input.value):Array.isArray(value)?input.value.split(',').map(s=>s.trim()).filter(Boolean):input.value;dirty=true;showJSON();status('有未保存的修改');};
  const note=document.createElement('small');note.textContent=key==='components'?'组件 ID 使用英文逗号分隔':key;
  box.append(label,input,note);$('fields').append(box);
 }showJSON();
}
async function load(file){if(dirty&&!confirm('放弃当前未保存的修改？'))return;try{const data=await request('/api/document?name='+encodeURIComponent(file));name=file;revision=data.revision;documentData=JSON.parse(data.content);index=0;dirty=false;$('search').disabled=!documentData.weapons;showItems();render();for(const b of $('files').children)b.className=b.dataset.file===name?'selected':'';status('已加载');}catch(e){status(e.message,true);}}
$('search').oninput=showItems;$('reload').onclick=()=>load(name);
$('save').onclick=async()=>{try{const data=await request('/api/document',{method:'POST',headers:{'Content-Type':'application/json','X-Config-Token':token},body:JSON.stringify({name,revision,content:JSON.stringify(documentData,null,2)})});revision=data.revision;documentData=JSON.parse(data.content);dirty=false;render();status(data.message);}catch(e){status(e.message,true);}};
window.addEventListener('beforeunload',e=>{if(dirty){e.preventDefault();e.returnValue='';}});
request('/api/catalog').then(data=>{token=data.token;for(const file of data.files){const b=document.createElement('button');b.textContent=file;b.dataset.file=file;b.onclick=()=>load(file);$('files').append(b);}return load(data.files[0]);}).catch(e=>status(e.message,true));
