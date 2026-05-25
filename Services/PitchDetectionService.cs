using musicmate.Utilities;
namespace musicmate.Services
{
    public class PitchDetectionService
    {
        // Reusable buffers to avoid per-frame allocations.
        // Safe because callers serialise access via _processLock.
        private static float[]? _windowBuf;
        private static double[]? _nsdfBuf;

        public static float ComputeRms(float[] buffer)
        {
            double sum = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                var v = buffer[i];
                sum += v * v;
            }
            return (float)Math.Sqrt(sum / buffer.Length);
        }

        // --- McLeod Pitch Method (MPM) implementation ---
        // Based on McLeod and Wyvill style NSDF + peak picking and parabolic interpolation.
        public static float DetectPitchMcLeod(float[] buffer, int length, int sampleRate)
        {
            try
            {
                // Quick RMS check to ignore silence (same approach as YIN branch)
                double sumsq = 0.0;
                double sum = 0.0;
                for (int i = 0; i < length; i++)
                {
                    float s = buffer[i];
                    sumsq += s * s;
                    sum += s;
                }
                double rms = Math.Sqrt(sumsq / length);
                if (rms < 0.0025) // allow lower RMS so very high notes with lower energy can still be detected
                    return 0;

                // Remove DC offset (helps NSDF) and apply a Hann window to reduce edge effects
                double mean = sum / length;
                if (_windowBuf == null || _windowBuf.Length < length)
                    _windowBuf = new float[length];
                var windowed = _windowBuf;
                double lengthM1 = length - 1;
                for (int i = 0; i < length; i++)
                {
                    float w = 0.5f * (1 - (float)Math.Cos(2 * Math.PI * i / lengthM1)); // Hann
                    windowed[i] = (float)((buffer[i] - mean) * w);
                }

                // Determine lag bounds: support very high freqs -> allow minLag = 2
                int minFreq = 60;           // lowest frequency to detect (Hz) - conservative default
                int maxFreq = Math.Min(sampleRate / 2, 8000); // cap upper freq search for stability
                int maxLag = Math.Min(length / 2, Math.Max(2, sampleRate / minFreq));
                int minLag = Math.Max(2, sampleRate / maxFreq);

                // Compute NSDF for lags [0..maxLag], tracking the global max and its index
                int nsdfLen = maxLag + 1;
                if (_nsdfBuf == null || _nsdfBuf.Length < nsdfLen)
                    _nsdfBuf = new double[nsdfLen];
                var nsdf = _nsdfBuf;
                double globalMax = double.MinValue;
                int globalMaxIdx = -1;
                for (int tau = 0; tau <= maxLag; tau++)
                {
                    double ac = 0.0;
                    double div = 0.0;
                    int n = length - tau;
                    for (int i = 0; i < n; i++)
                    {
                        ac += windowed[i] * windowed[i + tau];
                        div += windowed[i] * windowed[i] + windowed[i + tau] * windowed[i + tau];
                    }
                    double val = div == 0 ? 0.0 : (2.0 * ac) / div;
                    nsdf[tau] = val;
                    if (tau >= minLag && val > globalMax)
                    {
                        globalMax = val;
                        globalMaxIdx = tau;
                    }
                }

                if (globalMax <= 0.0) return 0;

                double threshold = Math.Max(0.5, 0.9 * globalMax); // require reasonably strong peak (tunable)

                int bestLag = -1;

                // Standard MPM: pick the FIRST peak above threshold (smallest lag = highest freq).
                // This prevents octave-down errors at high frequencies where subharmonic
                // peaks at 2x lag can have marginally higher NSDF values.
                for (int tau = minLag + 1; tau < maxLag; tau++)
                {
                    if (nsdf[tau] > nsdf[tau - 1] && nsdf[tau] >= nsdf[tau + 1] && nsdf[tau] > threshold)
                    {
                        bestLag = tau;
                        break;
                    }
                }

                // If no peak found above threshold, relax threshold and try the first peak again
                if (bestLag == -1)
                {
                    threshold = Math.Max(0.3, 0.75 * globalMax);
                    for (int tau = minLag + 1; tau < maxLag; tau++)
                    {
                        if (nsdf[tau] > nsdf[tau - 1] && nsdf[tau] >= nsdf[tau + 1] && nsdf[tau] > threshold)
                        {
                            bestLag = tau;
                            break;
                        }
                    }
                }

                if (bestLag <= 0)
                {
                    // fallback: use the tracked global-max index
                    if (globalMaxIdx > minLag) bestLag = globalMaxIdx;
                    else return 0;
                }

                // Low-end overtone guard: for instruments such as piano, the 2nd harmonic
                // can dominate the NSDF and be selected by the first-peak scan instead of
                // the fundamental (e.g. piano D3 detected as D4 because the D4-period peak
                // appears at a shorter lag and scores above threshold first).
                //
                // If the first-peak lag is less than half the global-max lag AND the global-max
                // is at least 90% as strong as the first-peak, the global-max is almost
                // certainly the true fundamental at a longer (lower) lag.  We switch to it.
                //
                // Guard: only apply when the candidate frequency is above 250 Hz (below that
                // the global-max heuristic is unreliable and risks false octave drops for
                // already-correct bass detections).
                if (globalMaxIdx > minLag
                    && bestLag < globalMaxIdx / 2
                    && nsdf[globalMaxIdx] >= 0.90 * nsdf[bestLag]
                    && (double)sampleRate / bestLag > 250.0)
                {
                    bestLag = globalMaxIdx;
                }

                // Sub-harmonic correction: if the chosen lag is actually a harmonic
                // (i.e. a lag at 2× or 3× also has a strong NSDF peak), prefer the
                // longer lag (lower frequency = true fundamental).  This prevents the
                // detector from locking onto a piano overtone — e.g. detecting the 3rd
                // partial of F5 at ~2094 Hz (lag ≈ 21) instead of F5 at ~698 Hz (lag ≈ 63).
                //
                // Guard: only apply when bestLag implies a frequency > 1600 Hz.
                //
                // Why 1600 Hz?
                //   • Piano/clarinet notes up to G6 (1568 Hz) are protected — no correction fires
                //     for any note at or below G6, so the detectable ceiling extends from E6 to ~G6.
                //   • The original F5 overtone problem (MPM locked onto F5's 3rd partial at
                //     ~2094 Hz instead of the fundamental at 698 Hz) is still corrected because
                //     2094 Hz > 1600 Hz.
                //   • Guard of 1200 Hz (previous value) was firing for F6 (1378 Hz), causing
                //     piano F6 to be detected as F5 due to strong octave sympathetic resonance.
                //
                // Using the ORIGINAL bestLag for every factor prevents cascading: without
                // this, factor=2 updating bestLag causes factor=3 to jump 6× instead of 3×,
                // producing absurd results such as B4 → E2.
                if ((double)sampleRate / bestLag > 1600.0)
                {
                    int originalBestLag = bestLag;
                    for (int factor = 2; factor <= 3; factor++)
                    {
                        int subLag = originalBestLag * factor;  // always relative to original
                        if (subLag >= maxLag) break;
                        // Search for a local peak near subLag (±2 samples)
                        int searchStart = Math.Max(minLag + 1, subLag - 2);
                        int searchEnd   = Math.Min(nsdfLen - 2, subLag + 2);
                        int peakAt  = -1;
                        double peakVal = double.MinValue;
                        for (int t = searchStart; t <= searchEnd; t++)
                        {
                            if (nsdf[t] > peakVal)
                            {
                                peakVal = nsdf[t];
                                peakAt  = t;
                            }
                        }
                        // Accept the sub-harmonic lag as the true fundamental if its NSDF
                        // peak is at least 85% as strong as the current best peak AND it is
                        // itself a local maximum (not just a shoulder or noise bump).
                        if (peakAt > minLag
                            && peakVal >= 0.85 * nsdf[originalBestLag]
                            && nsdf[peakAt] > nsdf[peakAt - 1]
                            && nsdf[peakAt] >= nsdf[peakAt + 1])
                        {
                            bestLag = peakAt;
                            break;  // found the fundamental — stop checking higher factors
                        }
                    }
                }

                // Parabolic interpolation around bestLag for sub-sample accuracy
                double refinedLag = bestLag;
                if (bestLag > 0 && bestLag < nsdfLen - 1)
                {
                    double y1 = nsdf[bestLag - 1];
                    double y2 = nsdf[bestLag];
                    double y3 = nsdf[bestLag + 1];
                    double denom = (y1 - 2.0 * y2 + y3);
                    if (Math.Abs(denom) > 1e-12)
                    {
                        double delta = 0.5 * (y1 - y3) / denom; // parabola vertex offset
                        refinedLag = bestLag + delta;
                    }
                }

                if (refinedLag <= 0) return 0;
                double freq = sampleRate / refinedLag;

                // Quick plausibility checks: frequency must be within expected range
                if (freq < 30 || freq > sampleRate / 2.0) return 0;

                return (float)freq;
            }
            catch (Exception ex)
            {
                Utils.Log($"DetectPitchMcLeod: {ex.Message}");
                return 0;
            }
        }
    }
}
