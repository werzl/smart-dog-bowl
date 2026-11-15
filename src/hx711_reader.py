
from machine import Pin, disable_irq, enable_irq
import time


class HX711:
    def __init__(self, dt_pin=4, sck_pin=5):
        """
        Initialise the HX711 amplifier.

        The HX711 uses two digital connections:
        - DT:  Data sent FROM the HX711 to the ESP32.
        - SCK: Clock controlled BY the ESP32.

        GPIO4 and GPIO5 match our physical wiring.
        """

        # Configure the data pin as an input.
        # The HX711 controls whether this pin is HIGH or LOW.
        self.dt = Pin(dt_pin, Pin.IN)

        # Configure the clock pin as an output.
        # Keep it LOW while we're not reading data.
        self.sck = Pin(sck_pin, Pin.OUT, value=0)

    def read(self):
        """
        Read one raw measurement from the HX711.

        The HX711 produces a signed 24-bit value.
        This is NOT a weight in grams yet.
        """

        # STEP 1: Wait for a measurement to become available.
        #
        # DT = HIGH (1): HX711 is still processing a measurement.
        # DT = LOW  (0): A new measurement is ready.
        #
        # Give up after 2 seconds rather than waiting forever.
        start = time.ticks_ms()

        while self.dt.value() == 1:
            if time.ticks_diff(time.ticks_ms(), start) > 2000:
                raise RuntimeError("HX711 timeout")

            time.sleep_ms(1)

        # STEP 2: Read the 24 bits of measurement data.
        #
        # The HX711 sends one bit each time we pulse SCK.
        #
        # A clock pulse is:
        #   1. Set SCK HIGH
        #   2. Read the data bit
        #   3. Set SCK LOW
        #
        # Each bit is added to the result using bitwise operations.
        value = 0

        # Temporarily disable interrupts during the transfer.
        # This prevents other tasks from holding SCK HIGH
        # too long, which could put the HX711 into power-down.
        irq_state = disable_irq()

        try:
            for _ in range(24):
                # Start a clock pulse.
                self.sck.value(1)
                time.sleep_us(1)

                # Shift previous bits left by one position,
                # then insert the new bit on the right.
                #
                # Example:
                #   value = 0101 (binary)
                #   New bit = 1
                #   Result  = 1011 (binary)
                value = (value << 1) | self.dt.value()

                # Finish the clock pulse.
                self.sck.value(0)
                time.sleep_us(1)

            # STEP 3: Send one extra clock pulse.
            #
            # The HX711 expects 25, 26 or 27 pulses in total.
            #
            # 25 pulses: Channel A, gain 128
            # 26 pulses: Channel B, gain 32
            # 27 pulses: Channel A, gain 64
            #
            # We use 25 pulses because channel A with gain 128
            # is the normal configuration for our load cell.
            self.sck.value(1)
            time.sleep_us(1)
            self.sck.value(0)

        finally:
            # Re-enable interrupts even if something goes wrong.
            enable_irq(irq_state)

        # STEP 4: Convert the result into a signed integer.
        #
        # The HX711 sends a 24-bit two's-complement number.
        # This means the measurement can be negative.
        #
        # Bit 23 (0x800000) indicates a negative value.
        # If it's set, subtract 2^24 to get the signed result.
        #
        # Example:
        #   0xFFFFFF becomes -1
        if value & 0x800000:
            value -= 0x1000000

        # Return the raw sensor measurement.
        return value

    def read_average(self, samples=10):
        """
        Take multiple measurements and return their average.

        Averaging reduces random electrical noise and helps
        produce more stable readings.

        Example:
            scale.read_average(5)

        This takes 5 measurements and returns their mean.
        """

        total = 0

        for _ in range(samples):
            total += self.read()

        return total / samples

