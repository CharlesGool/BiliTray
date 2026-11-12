(function(on){
  var st = document.getElementById('__bt_power');
  if (!on) {
    if (st) st.remove();
    window.__btPowerUrl = null;
    try { var p0 = JSON.parse(localStorage.getItem('bpx_player_profile') || '{}'); if (p0.media && p0.media.quality === 32) { p0.media.quality = 0; localStorage.setItem('bpx_player_profile', JSON.stringify(p0)); } } catch (e) {}
    return 'off';
  }
  if (!st) {
    st = document.createElement('style'); st.id = '__bt_power';
    st.textContent = '*,*::before,*::after{animation-duration:0s!important;animation-delay:0s!important;animation-iteration-count:1!important;transition:none!important;scroll-behavior:auto!important}';
    (document.head || document.documentElement).appendChild(st);
  }
  try { var p = JSON.parse(localStorage.getItem('bpx_player_profile') || '{}'); p.media = p.media || {}; if (p.media.quality !== 32) { p.media.quality = 32; localStorage.setItem('bpx_player_profile', JSON.stringify(p)); } } catch (e) {}
  if (window.__btPowerUrl === location.href) return 'same';
  window.__btPowerUrl = location.href;
  var n = 0, t = setInterval(function(){
    if (++n > 60) { clearInterval(t); return; }
    var pl = window.player;
    if (!pl || !pl.getSupportedQualityList) return;
    var list = pl.getSupportedQualityList() || [];
    if (!list.length) return;
    var best = null;
    list.forEach(function(q){ if (q <= 32 && (best === null || q > best)) best = q; });
    if (best === null) best = Math.min.apply(null, list);
    try { var cur = pl.getQuality(); if (cur.realQ !== best || cur.nowQ !== best) pl.requestQuality(best); } catch (e) {}
    try { if (pl.danmaku && pl.danmaku.isOpen && pl.danmaku.isOpen()) pl.danmaku.close(); } catch (e) {}
    clearInterval(t);
  }, 500);
  return 'applied';
})(__ON__);
