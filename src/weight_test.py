
import json
import time
from hx711_reader import HX711

# Initialise the sensor
scale = HX711(dt_pin=4, sck_pin=5)

# Load the calibration values saved previously
with open("calibration.json", "r") as file:
    calibration = json.load(file)

zero_offset = calibration["zero_offset"]
factor = calibration["calibration_factor"]

print("Smart Dog Bowl - Weight Monitor")
print("--------------------------------")

while True:
    # Average 10 readings for stability
    raw = scale.read_average(10)

    # Convert the raw measurement into grams
    weight_grams = (raw - zero_offset) / factor

    # Display the result
    print("Weight: {:.1f} g".format(weight_grams))

    time.sleep(1)

