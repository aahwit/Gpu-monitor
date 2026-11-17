const state={summary:null,devices:[],alerts:[],selected:null,history:[]};
const $=selector=>document.querySelector(selector);
const els={server:$('#server-status'),serverWrap:$('.server-state'),online:$('#online-count'),total:$('#total-count'),critical:$('#critical-count'),warnings:$('#warning-caption'),average:$('#average-temp'),rows:$('#device-rows'),alerts:$('#alert-list'),alertCount:$('#alert-count'),search:$('#device-search'),drawer:$('#device-drawer'),backdrop:$('#drawer-backdrop'),drawerTitle:$('#drawer-title'),drawerSubtitle:$('#drawer-subtitle'),drawerMetrics:$('#drawer-metrics'),facts:$('#device-facts'),canvas:$('#history-chart'),chartEmpty:$('#chart-empty')};
const escapeHtml=value=>String(value??'').replace(/[&<>'"]/g,char=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[char]));
const pct=(used,total)=>total>0?used/total*100:0;
const formatBytes=value=>{const n=Number(value||0);if(n>=1073741824)return`${(n/1073741824).toFixed(n>=10737418240?0:1)} GB`;if(n>=1048576)return`${(n/1048576).toFixed(0)} MB`;return`${(n/1024).toFixed(0)} KB`};
const timeAgo=value=>{if(!value)return'Never';const seconds=Math.max(0,(Date.now()-new Date(value).getTime())/1000);if(seconds<60)return'Just now';if(seconds<3600)return`${Math.floor(seconds/60)}m ago`;if(seconds<86400)return`${Math.floor(seconds/3600)}h ago`;return`${Math.floor(seconds/86400)}d ago`};
const tempClass=value=>Number(value)>=87?'hot':Number(value)>=78?'warm':'';

async function api(path){const response=await fetch(path,{cache:'no-store'});if(!response.ok)throw new Error(`${response.status} ${response.statusText}`);return response.json()}
async function loadDashboard(){
  try{
    const [summary,devices,alerts]=await Promise.all([api('/api/v1/summary'),api('/api/v1/devices'),api('/api/v1/alerts?status=open')]);
    state.summary=summary;state.devices=devices.devices||[];state.alerts=alerts.alerts||[];
    els.server.textContent='Live';els.serverWrap.className='server-state online';renderSummary();renderDevices();renderAlerts();
    if(state.selected){const updated=state.devices.find(item=>item.install_id===state.selected.install_id);if(updated){state.selected=updated;renderDrawer()}}
  }catch(error){els.server.textContent='Offline';els.serverWrap.className='server-state offline';console.error(error)}
}
function renderSummary(){const s=state.summary;els.online.textContent=s.online;els.total.textContent=s.total;els.critical.textContent=s.critical_alerts;els.warnings.textContent=`${s.warning_alerts} warnings`;els.average.textContent=`${Number(s.average_temperature_c).toFixed(1)}°C`;$('#online-caption').textContent=`${s.total?Math.round(s.online/s.total*100):0}% of fleet available`}
function renderDevices(){
  const query=els.search.value.trim().toLowerCase();
  const devices=state.devices.filter(item=>[item.gpu_name,item.app_version,item.hardware_id_short].join(' ').toLowerCase().includes(query)).sort((a,b)=>Number(b.alert_severity==='critical')-Number(a.alert_severity==='critical')||Number(b.online)-Number(a.online));
  if(!devices.length){els.rows.innerHTML='<tr><td colspan="8" class="empty">No matching devices</td></tr>';return}
  els.rows.innerHTML=devices.map(device=>{
    const vram=pct(device.vram_used_bytes,device.vram_total_bytes),ram=pct(device.system_ram_used_bytes,device.system_ram_total_bytes),severity=device.alert_severity;
    return `<tr data-device="${escapeHtml(device.install_id)}" tabindex="0"><td><div class="device-name"><span class="gpu-chip">GPU</span><div><b>${escapeHtml(device.gpu_name)} · ${formatBytes(device.vram_total_bytes)}</b><small>GPU-${escapeHtml(device.hardware_id_short)}</small></div></div></td><td><span class="status-pill ${device.online?'online':''}">${device.online?'Online':'Offline'}</span>${severity?` <span class="health-pill ${escapeHtml(severity)}">${escapeHtml(severity)}</span>`:''}</td><td class="${tempClass(device.gpu_temp_c)}"><b>${Number(device.gpu_temp_c||0).toFixed(0)}°C</b></td><td class="bar-cell"><span>${Number(device.gpu_load_pct||0).toFixed(0)}%</span><div class="mini-bar"><i style="width:${Math.min(100,device.gpu_load_pct||0)}%"></i></div></td><td class="bar-cell"><span>${vram.toFixed(0)}%</span><div class="mini-bar"><i style="width:${Math.min(100,vram)}%"></i></div></td><td>${ram.toFixed(0)}%</td><td>${escapeHtml(device.app_version)}</td><td>${timeAgo(device.last_seen)}</td></tr>`
  }).join('');
  els.rows.querySelectorAll('tr[data-device]').forEach(row=>{const open=()=>openDevice(row.dataset.device);row.addEventListener('click',open);row.addEventListener('keydown',event=>{if(event.key==='Enter')open()})})
}
function renderAlerts(){
  els.alertCount.textContent=state.alerts.length;
  if(!state.alerts.length){els.alerts.innerHTML='<div class="empty-card">All monitored devices are healthy.</div>';return}
  els.alerts.innerHTML=state.alerts.map(alert=>`<article class="alert-card ${escapeHtml(alert.severity)}"><h3>${escapeHtml(alert.title)}</h3><p>${escapeHtml(alert.message)}</p><div class="alert-meta"><span>GPU-${escapeHtml(String(alert.install_id).slice(0,8))}</span><span>${timeAgo(alert.last_seen)}</span></div></article>`).join('')
}
async function openDevice(id){
  state.selected=state.devices.find(item=>item.install_id===id);if(!state.selected)return;
  els.backdrop.hidden=false;els.drawer.classList.add('open');els.drawer.setAttribute('aria-hidden','false');renderDrawer();
  try{const result=await api(`/api/v1/devices/${encodeURIComponent(id)}/history?hours=24`);state.history=result.samples||[];drawHistory()}catch(error){state.history=[];drawHistory()}
}
function closeDrawer(){els.drawer.classList.remove('open');els.drawer.setAttribute('aria-hidden','true');els.backdrop.hidden=true;state.selected=null}
function renderDrawer(){
  const d=state.selected;if(!d)return;const vram=pct(d.vram_used_bytes,d.vram_total_bytes),ram=pct(d.system_ram_used_bytes,d.system_ram_total_bytes);
  els.drawerTitle.textContent=`${d.gpu_name} · ${formatBytes(d.vram_total_bytes)}`;els.drawerSubtitle.textContent=`GPU-${d.hardware_id_short} · ${d.online?'Online':'Offline'} · last seen ${timeAgo(d.last_seen)}`;
  els.drawerMetrics.innerHTML=[['Temperature',`${Number(d.gpu_temp_c||0).toFixed(0)}°C`],['GPU load',`${Number(d.gpu_load_pct||0).toFixed(0)}%`],['VRAM',`${vram.toFixed(0)}%`],['System RAM',`${ram.toFixed(0)}%`]].map(([label,value])=>`<div class="drawer-metric"><span>${label}</span><strong>${value}</strong></div>`).join('');
  els.facts.innerHTML=[['Driver',d.gpu_driver],['GPU Monitor version',d.app_version],['Operating system',d.os_version],['First seen',new Date(d.first_seen).toLocaleString()],['Power',`${Number(d.power_w||0).toFixed(0)} W`],['Fan',`${Number(d.fan_pct||0).toFixed(0)}%`],['CPU load',`${Number(d.cpu_load_pct||0).toFixed(0)}%`],['Installation ID',d.install_id]].map(([label,value])=>`<div class="fact"><span>${escapeHtml(label)}</span><b>${escapeHtml(value)}</b></div>`).join('')
}
function drawHistory(){
  const canvas=els.canvas,rect=canvas.getBoundingClientRect(),scale=window.devicePixelRatio||1;canvas.width=Math.max(1,Math.floor(rect.width*scale));canvas.height=Math.floor(230*scale);const ctx=canvas.getContext('2d');ctx.scale(scale,scale);const width=rect.width,height=230,pad={l:34,r:10,t:12,b:24};ctx.clearRect(0,0,width,height);
  if(state.history.length<2){canvas.hidden=true;els.chartEmpty.hidden=false;return}canvas.hidden=false;els.chartEmpty.hidden=true;
  ctx.strokeStyle='#1d343b';ctx.lineWidth=1;ctx.fillStyle='#6f878a';ctx.font='9px Segoe UI';for(let i=0;i<=4;i++){const y=pad.t+(height-pad.t-pad.b)*i/4;ctx.beginPath();ctx.moveTo(pad.l,y);ctx.lineTo(width-pad.r,y);ctx.stroke();ctx.fillText(`${100-i*25}`,4,y+3)}
  const series=[{color:'#ff5263',value:s=>Number(s.gpu_temp_c||0)},{color:'#35d1cf',value:s=>Number(s.gpu_load_pct||0)},{color:'#ffbd59',value:s=>pct(s.vram_used_bytes,s.vram_total_bytes)},{color:'#4aa8ff',value:s=>pct(s.system_ram_used_bytes,s.system_ram_total_bytes)}];
  series.forEach(line=>{ctx.beginPath();ctx.strokeStyle=line.color;ctx.lineWidth=2;state.history.forEach((sample,index)=>{const x=pad.l+(width-pad.l-pad.r)*index/(state.history.length-1),y=pad.t+(height-pad.t-pad.b)*(1-Math.min(100,line.value(sample))/100);index?ctx.lineTo(x,y):ctx.moveTo(x,y)});ctx.stroke()});
  ctx.fillStyle='#6f878a';ctx.fillText(new Date(state.history[0].recorded_at).toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'}),pad.l,height-7);const end=new Date(state.history.at(-1).recorded_at).toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'});ctx.fillText(end,width-pad.r-36,height-7)
}
$('#refresh').addEventListener('click',loadDashboard);els.search.addEventListener('input',renderDevices);$('#drawer-close').addEventListener('click',closeDrawer);els.backdrop.addEventListener('click',closeDrawer);window.addEventListener('keydown',event=>{if(event.key==='Escape')closeDrawer()});window.addEventListener('resize',()=>{if(state.selected)drawHistory()});
loadDashboard();setInterval(loadDashboard,15000);
