
import json
import time
from hx711_reader import HX711

# Initialise our HX711 using the existing driver
scale = HX711(dt_pin=4, sck_pin=5)

print("Smart Dog Bowl - Calibration")
print("----------------------------")

# STEP 1: Measure the empty bowl.
#
# This is our tare weight. Any measurement at
# this level should be interpreted as 0 grams.

input("Place the empty bowl on the scale. Press Enter...")

# Allow the load cell to settle before measuring.
time.sleep(2)

# Average 20 readings to reduce noise.
zero_offset = scale.read_average(20)

print("Zero offset:", zero_offset)


# STEP 2: Add a known weight.
#
# We need to establish how much the HX711's
# output changes for each gram added.

known_weight = float(
    input("Enter calibration weight in grams (e.g. 500): ")
)

if known_weight <= 0:
    raise ValueError("Weight must be greater than zero")

input("Add the known weight. Press Enter...")

time.sleep(2)

loaded_reading = scale.read_average(20)

print("Loaded reading:", loaded_reading)


# STEP 3: Calculate the calibration factor.
#
# The difference between loaded and unloaded
# readings is the raw measurement for our weight.
#
# Divide that by the known mass to calculate
# the number of raw units per gram.

difference = loaded_reading - zero_offset

if difference == 0:
    raise RuntimeError("No change in weight detected")

calibration_factor = difference / known_weight

print("Calibration factor:", calibration_factor)


# STEP 4: Save the calibration values.
#
# We store them as JSON in the ESP32's flash.
# This means they survive a reboot, so we
# don't need to calibrate every time.

calibration = {
    "zero_offset": zero_offset,
    "calibration_factor": calibration_factor
}

with open("calibration.json", "w") as file:
    json.dump(calibration, file)

print("Calibration saved successfully!")

