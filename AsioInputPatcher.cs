﻿using System;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace loopman
{
    public class AsioInputPatcher : ISampleProvider
    {

        public float[] channelPeakIn; // 0-1 operating range
        public float[] channelPeakOut; // 0-1 operating range
        public float[] recording1;
        public int iRecord;
        public int iRecordMax;
        public int iPlay;
        public bool isRecording;
        public bool isPlaying;
        private int loopStart;
        private int loopEnd;

        private readonly int outputChannels;
        private readonly int inputChannels;
        private readonly float[,] routingMatrix;
        private float[] mixBuffer;
        private int sampleRate;

        private AudioFileReader clickFile0 = new AudioFileReader("Bb Click.wav");
        private AudioFileReader clickFile1 = new AudioFileReader("Eb Click.wav");
        private float[] clickBuffer0;
        private float[] clickBuffer1;
        private float[] clickMix;
        private int clickOffset, clickOffsetMax;
        private bool firstBeatClick;
        private bool metronomeClick;

        public float clickPan { get; set; }
        public float clickVolume { get; set; }



        public AsioInputPatcher(int _sampleRate, int inputChannels, int outputChannels)
        {
            sampleRate = _sampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, outputChannels);
            this.outputChannels = outputChannels;
            this.inputChannels = inputChannels;
            routingMatrix = new float[inputChannels, outputChannels];
            channelPeakIn = BufferHelpers.Ensure(channelPeakIn, inputChannels);
            channelPeakOut = BufferHelpers.Ensure(channelPeakOut, outputChannels);

            // initial routing is each input straight to the same number output
            for (int n = 0; n < Math.Min(inputChannels, outputChannels); n++)
            {
                routingMatrix[n, n] = 1.0f;
            }

            routingMatrix[0, 0] = 0.5f;
            routingMatrix[0, 1] = 0.5f;
            routingMatrix[1, 0] = 0.5f;
            routingMatrix[1, 1] = 0.5f;

            // read-in the metronome click waves
            clickFile0.Position = 0;
            clickBuffer0 = BufferHelpers.Ensure(clickBuffer0, (int)clickFile0.Length);
            clickFile0.ToSampleProvider().Read(clickBuffer0, 0, (int)clickFile0.Length);

            clickFile1.Position = 0;
            clickBuffer1 = BufferHelpers.Ensure(clickBuffer1, (int)clickFile1.Length);
            clickFile1.ToSampleProvider().Read(clickBuffer1, 0, (int)clickFile1.Length);

            // setup the metronome pan
            clickMix = BufferHelpers.Ensure(clickMix, outputChannels);
            for (int i = 0; i < clickMix.Length; i++) clickMix[i] = 0;

            metronomeClick = false;

            isPlaying = isRecording = false;
        }

        public void Click(bool _firstBeatClick)
        {
            firstBeatClick = _firstBeatClick;
            clickOffset = 0;
            clickOffsetMax = (firstBeatClick) ? (int)clickFile0.Length - 1 : (int)clickFile1.Length - 1;
            clickMix[0] = clickVolume * (clickPan - 1) / -2;
            clickMix[1] = clickVolume * (clickPan + 1) / 2;
            metronomeClick = true;
        }

        public void InitRecording(double seconds)
        {
            iRecordMax = (int)(1 + sampleRate * seconds * outputChannels);
            recording1 = BufferHelpers.Ensure(recording1, iRecordMax);
            Record();
        }

        public void Record()
        {
            iRecord = 0;
            isRecording = true;
        }

        public void Play()
        {
            iPlay = loopStart;
            isPlaying = true;
        }

        public void Stop()
        {
            iPlay = loopStart;
            isRecording = isPlaying = false;
        }

        public void MarkLoopStart()
        {
            loopStart = iRecord;
        }

        public void MarkLoopEnd()
        {
            loopEnd = iRecord-1;
        }


        private bool ngEnabled = true;
        private float ngThreshold;
        private int ngReleaseTime = 100;
        private int[] ngReleaseSamples = { 100, 100 };
        private int[] ngState = { 0, 0 };

        public void EnableNoiseGate(bool enabled)
        {
            ngEnabled = enabled;
        }

        public int GetNoiseGateThreshold()
        {
            return (int)((ngThreshold - 0.001f) * 10000f);
        }

        // **Get to -20 to -80 dB
        public void SetNoiseGateThreshold(int percent)
        {
            ngThreshold = 0.001f + (float)percent / 10000f;
        }

        public void ResetPeakIn()
        {
            // reset VU meter peaks
            for (int inputChannel = 0; inputChannel < inputChannels; inputChannel++)
            {
                channelPeakIn[inputChannel] = 0;
            }
        }

        public void ResetPeakOut()
        {
            // reset VU meter peaks
            for (int outputChannel = 0; outputChannel < outputChannels; outputChannel++)
            {
                channelPeakOut[outputChannel] = 0;
            }
        }


        // here we get given all the input channels we recorded from
        public void ProcessBuffer(IntPtr[] inBuffers, IntPtr[] outBuffers, int sampleCount, AsioSampleType sampleType)
        {
            Func<IntPtr, int, float> getInputSample;
            if (sampleType == AsioSampleType.Int32LSB)          getInputSample = GetInputSampleInt32LSB;
            else if (sampleType == AsioSampleType.Int16LSB)     getInputSample = GetInputSampleInt16LSB;
            else if (sampleType == AsioSampleType.Int24LSB)     getInputSample = GetInputSampleInt24LSB;
            else if (sampleType == AsioSampleType.Float32LSB)   getInputSample = GetInputSampleFloat32LSB;
            else throw new ArgumentException($"Unsupported ASIO sample type {sampleType}");

            int offset = 0;
            mixBuffer = BufferHelpers.Ensure(mixBuffer, sampleCount * outputChannels);

            for (int n = 0; n < sampleCount; n++)
            {
                for (int outputChannel = 0; outputChannel < outputChannels; outputChannel++)
                {
                    mixBuffer[offset] = 0.0f;
                    for (int inputChannel = 0; inputChannel < inputChannels; inputChannel++)
                    {
                        float inSample = getInputSample(inBuffers[inputChannel], n);
                        float inSampleAbs = Math.Abs(inSample);

                        // Get peak for VU meter
                        if (inSampleAbs > channelPeakIn[inputChannel])
                        {
                            channelPeakIn[inputChannel] = inSampleAbs;
                        }

                        // Noise Gate
                        if (ngEnabled && (inputChannel < 2))
                        {
                            switch (ngState[inputChannel])
                            {
                                case 0: // Gate Open
                                    if (inSampleAbs <= ngThreshold)
                                    {
                                        ngReleaseSamples[inputChannel] = ngReleaseTime;
                                        ngState[inputChannel] = 1;
                                    }
                                    break;

                                case 1: // Gate Closing
                                    if (inSampleAbs > ngThreshold) ngState[inputChannel] = 0;
                                    else if (--ngReleaseSamples[inputChannel] <= 0) ngState[inputChannel] = 2;
                                    break;

                                case 2: // Gate Closed
                                    if (inSampleAbs > ngThreshold) ngState[inputChannel] = 0;
                                    else inSample = 0;
                                    break;

                                default:
                                    break;
                            }
                        }

                        // mix in the desired amount
                        var amount = routingMatrix[inputChannel, outputChannel];
                        if (amount > 0) mixBuffer[offset] += amount * inSample;
                    }

                    if (isRecording && (iRecord < iRecordMax))
                    {
                        recording1[iRecord++] = mixBuffer[offset];
                    }
                    if (isPlaying)
                    {
                        if (iPlay > loopEnd) iPlay = loopStart;
                        mixBuffer[offset] += recording1[iPlay++];
                    }

                    // mix in the metronome click wave (which is a mono wave)
                    if (metronomeClick)
                    {
                        mixBuffer[offset] += (float)(clickMix[outputChannel] *
                            (firstBeatClick ? clickBuffer0[clickOffset >> 1] : clickBuffer1[clickOffset >> 1]));
                        if (++clickOffset > clickOffsetMax) metronomeClick = false;
                    }

                    float outSampleAbs = Math.Abs(mixBuffer[offset]);

                    // Get peak for VU meter
                    if (outSampleAbs > channelPeakOut[outputChannel])
                    {
                        channelPeakOut[outputChannel] = outSampleAbs;
                    }

                    offset++;
                }
            }


            Action<IntPtr, int, float> setOutputSample;
            if (sampleType == AsioSampleType.Int32LSB)          setOutputSample = SetOutputSampleInt32LSB;
            else if (sampleType == AsioSampleType.Int16LSB)     setOutputSample = SetOutputSampleInt16LSB;
            else if (sampleType == AsioSampleType.Int24LSB)     throw new InvalidOperationException("Not supported");
            else if (sampleType == AsioSampleType.Float32LSB)   setOutputSample = SetOutputSampleFloat32LSB;
            else throw new ArgumentException($"Unsupported ASIO sample type {sampleType}");


            // now write to the output buffers
            offset = 0;
            for (int n = 0; n < sampleCount; n++)
            {
                for (int outputChannel = 0; outputChannel < outputChannels; outputChannel++)
                {
                    setOutputSample(outBuffers[outputChannel], n, mixBuffer[offset++]);
                }
            }
        }

        private unsafe void SetOutputSampleInt32LSB(IntPtr buffer, int n, float value)
        {
            *((int*)buffer + n) = (int)(value * int.MaxValue);
        }

        private unsafe float GetInputSampleInt32LSB(IntPtr inputBuffer, int n)
        {
            return *((int*)inputBuffer + n) / (float)int.MaxValue;
        }

        private unsafe float GetInputSampleInt16LSB(IntPtr inputBuffer, int n)
        {
            return *((short*)inputBuffer + n) / (float)short.MaxValue;
        }

        private unsafe void SetOutputSampleInt16LSB(IntPtr buffer, int n, float value)
        {
            *((short*)buffer + n) = (short)(value * short.MaxValue);
        }

        private unsafe float GetInputSampleInt24LSB(IntPtr inputBuffer, int n)
        {
            byte* pSample = (byte*)inputBuffer + n * 3;
            int sample = pSample[0] | (pSample[1] << 8) | ((sbyte)pSample[2] << 16);
            return sample / 8388608.0f;
        }

        private unsafe float GetInputSampleFloat32LSB(IntPtr inputBuffer, int n)
        {
            return *((float*) inputBuffer + n);
        }

        private unsafe void SetOutputSampleFloat32LSB(IntPtr buffer, int n, float value)
        {
            *((float*) buffer + n) = value;
        }

        public float[,] RoutingMatrix => routingMatrix;

        // immediately after SetInputSamples, we are now asked for all the audio we want
        // to write to the soundcard
        public int Read(float[] buffer, int offset, int count)
        {
            throw new InvalidOperationException("Should not be called");
        }

        public WaveFormat WaveFormat { get; }
    }
}