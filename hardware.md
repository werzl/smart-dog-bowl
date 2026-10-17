# Hardware and assembly

## Bill of materials (indicative UK budget)

| Item | Specification | Estimated cost |
|---|---|---:|
| Controller | ESP32-C3 development board supporting MicroPython | £5–12 |
| Scale sensor | 5kg single-point bar load cell (increase capacity for large bowls) | £4–9 |
| ADC | 3.3V-compatible HX711 module | £2–8 |
| Battery | Protected 3.7V Li-ion pack, ~2500–3500mAh | £10–20 |
| Charger | Li-ion charging board with power-path management | £8–15 |
| Regulator | Stable 3.3V regulator sized for ESP32 Wi-Fi current spikes | £3–8 |
| Mechanical | Rigid plates, raised frame, bolts, feet, stops | £10–25 |
| Enclosure | Splash-resistant project box and strain relief | £5–10 |

These are estimates, not confirmed supplier quotes. Choose an appropriately protected, preassembled battery and follow manufacturer guidance on charging current and wiring.

## Electrical wiring

**ESP32-C3 → HX711 (example pin assignment)**

| ESP32-C3 | HX711 |
|---|---|
| 3V3 | VCC (if the HX711 board supports 3.3V) |
| GND | GND |
| GPIO4 | DOUT/DT |
| GPIO5 | PD_SCK/SCK |

**HX711 → four-wire load cell**

| HX711 | Common colour (verify actual sensor) |
|---|---|
| E+ | Red |
| E- | Black |
| A+ | Green |
| A- | White |

**Power:** protected Li-ion → compatible charger/power-path board → regulated 3.3V → ESP32/HX711. Share ground. Charger output is *not* necessarily regulated to 3.3V. Check board-specific pins, voltage ratings and polarity before energising. An optional load switch can power-gate the HX711 for sleep optimisation.

## Mechanical assembly

1. Build a wide, stable raised frame sized to the dog; prevent tipping.
2. Mount the fixed end of the load cell to a rigid base bracket, following the sensor's loading direction.
3. Mount the floating top plate to the sensing end, leaving ~3–5mm clearance around the fixed stand.
4. Add overload stops with a small gap during normal loading.
5. Place a removable stainless-steel bowl on the platform; ensure the bowl never contacts the fixed frame.
6. Route sensor wiring away from pinch points and protect it from chewing.
7. Fit electronics and battery in a secured splash-resistant enclosure below the bowl, away from water paths; use drip loops and strain relief.
8. Bench-test on USB before integrating the battery and charger.

## Calibration and verification

- Zero with an empty platform; calibrate with known weights, e.g. 500g.
- Save the empty-bowl tare and calibration factor in nonvolatile configuration; do not auto-tare on every reboot.
- Check multiple loading positions and weights, and compare 250ml of added water against ~250g.
- Test bowl removal, spills, vibration, refills, cable movement and stand tipping.
- Measure sleep, wake, sensor and Wi-Fi upload currents to estimate battery runtime.

**Accuracy caveat:** the load cell detects water lost from the bowl, not necessarily swallowed. A proximity sensor may improve confidence in future versions.
