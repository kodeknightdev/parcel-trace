// booking form map picker
(function () {
  const el = document.getElementById('picker');
  if (!el || !window.L) return;

  const field = (name) => document.getElementById(`Input_${name}`);
  const read = (prefix) => [parseFloat(field(`${prefix}Lat`).value.replace(',', '.')), parseFloat(field(`${prefix}Lng`).value.replace(',', '.'))];

  const map = L.map(el).setView([52.515, 13.39], 12);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; OpenStreetMap contributors',
  }).addTo(map);

  const markers = {
    Pickup: L.circleMarker(read('Pickup'), { radius: 9, color: '#0d6efd', fillOpacity: 0.8 }).addTo(map).bindTooltip('Pick-up', { permanent: true }),
    Dropoff: L.circleMarker(read('Dropoff'), { radius: 9, color: '#198754', fillOpacity: 0.8 }).addTo(map).bindTooltip('Drop-off', { permanent: true }),
  };
  map.fitBounds(L.latLngBounds([markers.Pickup.getLatLng(), markers.Dropoff.getLatLng()]).pad(0.4));

  map.on('click', (e) => {
    const prefix = document.getElementById('mode-dropoff').checked ? 'Dropoff' : 'Pickup';
    markers[prefix].setLatLng(e.latlng);
    field(`${prefix}Lat`).value = e.latlng.lat.toFixed(5);
    field(`${prefix}Lng`).value = e.latlng.lng.toFixed(5);
  });
})();
