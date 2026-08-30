package com.signvr.recording;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.os.Build;

import java.io.File;
import java.nio.ByteBuffer;

/** Encodes RGB24 frames to H.264 in an MP4 container on the Quest. */
public final class SignVRMp4Recorder {
    private static final Object LOCK = new Object();
    private static final String MIME = "video/avc";
    private static final int COLOR_FORMAT_YUV420_PLANAR = 19;
    private static final int COLOR_FORMAT_YUV420_SEMIPLANAR = 21;
    private static final int COLOR_FORMAT_YUV420_FLEXIBLE = 0x7F420888;

    private static MediaCodec codec;
    private static MediaMuxer muxer;
    private static int trackIndex = -1;
    private static boolean muxerStarted;
    private static int colorFormat;
    private static int frameWidth;
    private static int frameHeight;
    private static byte[] yuvBuffer;
    private static String lastError = "";

    private SignVRMp4Recorder() {
    }

    public static boolean start(
            String outputPath,
            int width,
            int height,
            int frameRate,
            int bitrate) {
        synchronized (LOCK) {
            stopLocked();
            lastError = "";
            if (Build.VERSION.SDK_INT < 18 || outputPath == null ||
                    width <= 0 || height <= 0 || frameRate <= 0) {
                lastError = "Unsupported video encoder configuration.";
                return false;
            }

            try {
                File output = new File(outputPath);
                File parent = output.getParentFile();
                if (parent != null && !parent.exists() && !parent.mkdirs()) {
                    throw new IllegalStateException("Could not create recording directory.");
                }
                if (output.exists() && !output.delete()) {
                    throw new IllegalStateException("Could not replace existing MP4.");
                }

                MediaCodecInfo encoder = findEncoder();
                if (encoder == null) {
                    throw new IllegalStateException("No H.264 encoder is available.");
                }
                colorFormat = chooseColorFormat(
                        encoder.getCapabilitiesForType(MIME).colorFormats);
                if (colorFormat == 0) {
                    throw new IllegalStateException("H.264 encoder has no YUV420 input format.");
                }

                MediaFormat format = MediaFormat.createVideoFormat(
                        MIME, width, height);
                format.setInteger(MediaFormat.KEY_COLOR_FORMAT, colorFormat);
                format.setInteger(MediaFormat.KEY_BIT_RATE, Math.max(1_000_000, bitrate));
                format.setInteger(MediaFormat.KEY_FRAME_RATE, frameRate);
                format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
                codec = MediaCodec.createByCodecName(encoder.getName());
                codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                codec.start();

                muxer = new MediaMuxer(
                        outputPath,
                        MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
                trackIndex = -1;
                muxerStarted = false;
                frameWidth = width;
                frameHeight = height;
                yuvBuffer = new byte[width * height * 3 / 2];
                return true;
            } catch (Exception exception) {
                lastError = exception.toString();
                stopLocked();
                return false;
            }
        }
    }

    public static boolean encodeRgb(byte[] rgb24, long presentationTimeUs) {
        synchronized (LOCK) {
            if (codec == null || rgb24 == null ||
                    rgb24.length != frameWidth * frameHeight * 3) {
                lastError = "Invalid RGB frame or encoder is stopped.";
                return false;
            }

            try {
                int inputIndex = codec.dequeueInputBuffer(0);
                if (inputIndex < 0) {
                    drainEncoder(false);
                    inputIndex = codec.dequeueInputBuffer(50_000);
                }
                if (inputIndex < 0) {
                    lastError = "H.264 encoder input queue is full.";
                    return false;
                }

                rgbToYuv420(rgb24, yuvBuffer, frameWidth, frameHeight, colorFormat);
                ByteBuffer input = codec.getInputBuffer(inputIndex);
                if (input == null || input.capacity() < yuvBuffer.length) {
                    lastError = "H.264 encoder input buffer is too small.";
                    return false;
                }
                input.clear();
                input.put(yuvBuffer);
                codec.queueInputBuffer(
                        inputIndex,
                        0,
                        yuvBuffer.length,
                        Math.max(0L, presentationTimeUs),
                        0);
                drainEncoder(false);
                return true;
            } catch (Exception exception) {
                lastError = exception.toString();
                return false;
            }
        }
    }

    public static void stop() {
        synchronized (LOCK) {
            stopLocked();
        }
    }

    public static String getLastError() {
        synchronized (LOCK) {
            return lastError;
        }
    }

    private static void stopLocked() {
        if (codec == null && muxer == null) {
            yuvBuffer = null;
            trackIndex = -1;
            muxerStarted = false;
            return;
        }

        try {
            if (codec != null) {
                int inputIndex = codec.dequeueInputBuffer(10_000);
                if (inputIndex >= 0) {
                    codec.queueInputBuffer(
                            inputIndex,
                            0,
                            0,
                            0,
                            MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                    drainEncoder(true);
                }
            }
        } catch (Exception exception) {
            if (lastError.length() == 0) {
                lastError = exception.toString();
            }
        } finally {
            try {
                if (codec != null) {
                    codec.stop();
                    codec.release();
                }
            } catch (Exception exception) {
                if (lastError.length() == 0) {
                    lastError = exception.toString();
                }
            }
            codec = null;
            try {
                if (muxer != null && muxerStarted) {
                    muxer.stop();
                }
                if (muxer != null) {
                    muxer.release();
                }
            } catch (Exception exception) {
                if (lastError.length() == 0) {
                    lastError = exception.toString();
                }
            }
            muxer = null;
            yuvBuffer = null;
            trackIndex = -1;
            muxerStarted = false;
        }
    }

    private static void drainEncoder(boolean endOfStream) {
        if (codec == null) {
            return;
        }

        MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();
        while (true) {
            int outputIndex = codec.dequeueOutputBuffer(bufferInfo, endOfStream ? 10_000 : 0);
            if (outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!endOfStream) {
                    return;
                }
                continue;
            }
            if (outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (muxerStarted) {
                    throw new IllegalStateException("Encoder format changed twice.");
                }
                trackIndex = muxer.addTrack(codec.getOutputFormat());
                muxer.start();
                muxerStarted = true;
                continue;
            }
            if (outputIndex < 0) {
                continue;
            }

            ByteBuffer output = codec.getOutputBuffer(outputIndex);
            if (output != null && bufferInfo.size > 0 && muxerStarted &&
                    (bufferInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0) {
                output.position(bufferInfo.offset);
                output.limit(bufferInfo.offset + bufferInfo.size);
                muxer.writeSampleData(trackIndex, output, bufferInfo);
            }
            boolean eos = (bufferInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            codec.releaseOutputBuffer(outputIndex, false);
            if (eos) {
                return;
            }
        }
    }

    private static MediaCodecInfo findEncoder() {
        MediaCodecList list = new MediaCodecList(MediaCodecList.ALL_CODECS);
        MediaCodecInfo[] infos = list.getCodecInfos();
        for (MediaCodecInfo info : infos) {
            if (!info.isEncoder()) {
                continue;
            }
            String[] types = info.getSupportedTypes();
            for (String type : types) {
                if (MIME.equalsIgnoreCase(type) &&
                        chooseColorFormat(info.getCapabilitiesForType(MIME).colorFormats) != 0) {
                    return info;
                }
            }
        }
        return null;
    }

    private static int chooseColorFormat(int[] formats) {
        for (int format : formats) {
            if (format == COLOR_FORMAT_YUV420_SEMIPLANAR) {
                return format;
            }
        }
        for (int format : formats) {
            if (format == COLOR_FORMAT_YUV420_PLANAR) {
                return format;
            }
        }
        for (int format : formats) {
            if (format == COLOR_FORMAT_YUV420_FLEXIBLE) {
                return COLOR_FORMAT_YUV420_SEMIPLANAR;
            }
        }
        return 0;
    }

    private static void rgbToYuv420(
            byte[] rgb,
            byte[] yuv,
            int width,
            int height,
            int format) {
        int frameSize = width * height;
        int yIndex = 0;
        int uIndex = frameSize;
        int vIndex = frameSize + frameSize / 4;
        boolean planar = format == COLOR_FORMAT_YUV420_PLANAR;

        for (int y = 0; y < height; y++) {
            int rgbRow = y * width * 3;
            for (int x = 0; x < width; x++) {
                int rgbIndex = rgbRow + x * 3;
                int r = rgb[rgbIndex] & 0xff;
                int g = rgb[rgbIndex + 1] & 0xff;
                int b = rgb[rgbIndex + 2] & 0xff;
                yuv[yIndex++] = (byte) clamp((66 * r + 129 * g + 25 * b + 128 >> 8) + 16);

                if ((y & 1) == 0 && (x & 1) == 0) {
                    int u = clamp((-38 * r - 74 * g + 112 * b + 128 >> 8) + 128);
                    int v = clamp((112 * r - 94 * g - 18 * b + 128 >> 8) + 128);
                    if (planar) {
                        yuv[uIndex++] = (byte) u;
                        yuv[vIndex++] = (byte) v;
                    } else {
                        yuv[uIndex++] = (byte) u;
                        yuv[uIndex++] = (byte) v;
                    }
                }
            }
        }
    }

    private static int clamp(int value) {
        return value < 0 ? 0 : Math.min(255, value);
    }
}
