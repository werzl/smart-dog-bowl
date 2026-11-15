
import time
from hx711_reader import HX711

scale = HX711(dt_pin=4, sck_pin=5)

print("Smart Dog Bowl — Scale Test")
print("---------------------------")

while True:
    weight = scale.read_average(5)

    print("Raw reading:", weight)

    time.sleep(1)
