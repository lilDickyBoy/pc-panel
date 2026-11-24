"""Generates the soft UI sound effects used by PC Panel (no third-party samples, so they're free to ship)."""
import numpy as np, wave, sys, os
SR = 44100
out = sys.argv[1] if len(sys.argv) > 1 else "."

def env(n, a=0.004, r=0.25):
    t = np.arange(n) / SR
    e = np.minimum(1, t / a) * np.exp(-t / r)
    return e

def tone(f, dur, r=0.25, harm=(1, .35, .12), a=0.004, vib=0):
    n = int(SR * dur); t = np.arange(n) / SR
    ph = 2 * np.pi * f * t + (vib * np.sin(2 * np.pi * 5 * t) if vib else 0)
    s = sum(h * np.sin(ph * (i + 1)) for i, h in enumerate(harm))
    return s * env(n, a, r)

def mix(parts, total):
    buf = np.zeros(int(SR * total))
    for start, sig in parts:
        i = int(SR * start); buf[i:i + len(sig)] += sig[:len(buf) - i]
    return buf

def save(name, x, gain=0.55):
    x = x / (np.max(np.abs(x)) + 1e-9) * gain
    fade = int(SR * 0.01); x[-fade:] *= np.linspace(1, 0, fade)
    with wave.open(os.path.join(out, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())

# rising two-note chime (iPhone-like charge)
save("charge_in", mix([(0, tone(880, .5, .18)), (.09, tone(1318.5, .7, .25))], .8))
# falling soft blip
save("charge_out", mix([(0, tone(1046.5, .35, .12)), (.08, tone(659.3, .45, .16))], .6), .45)
# glassy notification
save("notify", mix([(0, tone(1567.98, .5, .2, (1, .2, .05))), (.07, tone(2093, .6, .22, (1, .15)))], .7), .45)
# connect: three quick rising notes
save("connect", mix([(0, tone(784, .25, .09)), (.07, tone(987.8, .25, .09)), (.14, tone(1174.7, .5, .2))], .7), .45)
# disconnect: descending
save("disconnect", mix([(0, tone(1174.7, .25, .09)), (.08, tone(880, .25, .09)), (.16, tone(587.3, .5, .18))], .75), .45)
# alert: double pulse
save("alert", mix([(0, tone(988, .22, .08, (1, .5, .3))), (.2, tone(988, .3, .1, (1, .5, .3)))], .6), .5)
# timer done: bright arpeggio
save("done", mix([(i * .11, tone(f, .6, .22)) for i, f in enumerate([1046.5, 1318.5, 1568, 2093])], 1.1), .5)
# low battery: low two-tone
save("low", mix([(0, tone(523.25, .35, .14)), (.18, tone(392, .5, .2))], .75), .5)
# file received: soft pop + sparkle
save("file", mix([(0, tone(660, .2, .05, (1, .3))), (.05, tone(1760, .4, .15, (1, .1)))], .5), .45)
