// tracking page map, polls every 15s
(function () {
  const el = document.getElementById('map');
  if (!el || !window.L) return;

  // fall back to Berlin
  const dest = el.dataset.destLat ? [parseFloat(el.dataset.destLat), parseFloat(el.dataset.destLng)] : [52.52, 13.405];
  const map = L.map(el).setView(dest, 13);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
  }).addTo(map);

  L.circle(dest, { radius: 600, color: '#198754', fillOpacity: 0.15 }).addTo(map).bindTooltip('Delivery area');

  const courierIcon = L.divIcon({ className: '', html: '<div style="font-size:1.8rem;line-height:1">🚚</div>', iconSize: [28, 28], iconAnchor: [14, 14] });
  let courier = null;

  function showCourier(loc) {
    if (!loc) return;
    const pos = [loc.lat, loc.lng];
    if (courier) {
      courier.setLatLng(pos);
    } else {
      courier = L.marker(pos, { icon: courierIcon }).addTo(map).bindTooltip('Courier');
      map.fitBounds(L.latLngBounds([pos, dest]).pad(0.3));
    }
  }

  if (el.dataset.courierLat) {
    showCourier({ lat: parseFloat(el.dataset.courierLat), lng: parseFloat(el.dataset.courierLng) });
  }

  const fmt = (iso) => new Date(iso).toLocaleString(undefined, { weekday: 'short', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
  const terminal = ['Delivered', 'Failed', 'Cancelled'];

  async function poll() {
    try {
      const res = await fetch(el.dataset.pollUrl, { headers: { accept: 'application/json' } });
      if (!res.ok) return;
      const data = await res.json();
      showCourier(data.lastLocation);
      if (data.eta) document.getElementById('eta').textContent = data.eta.minutesRemaining == null ? fmt(data.eta.etaAt) : `${fmt(data.eta.etaAt)} (${data.eta.minutesRemaining} min)`;
      if (data.lastLocation) document.getElementById('last-seen').textContent = `Courier last seen ${fmt(data.lastLocation.recordedAt)}`;
      const badge = document.getElementById('status-badge');
      if (badge && badge.dataset.status !== data.status) {
        window.location.reload(); // status changed, re-render
        return;
      }
      if (terminal.includes(data.status)) return;
    } catch {
      // ignore, retry next tick
    }
    setTimeout(poll, 15000);
  }

  setTimeout(poll, 15000);
})();
