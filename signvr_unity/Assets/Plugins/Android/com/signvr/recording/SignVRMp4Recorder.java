package com.signvr.recording;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.os.Build;
import android.util.Log;

import java.io.File;
import java.nio.ByteBuffer;

/** Encodes GPU-produced NV12 frames to H.264 MP4 on the Quest. */
public final class SignVRMp4Recorder {
    private static final Object LOCK = new Object();
    private static final String TAG = "SignVRMp4Recorder";
    private static final String MIME = "video/avc";
    private static final long EOS_DRAIN_TIMEOUT_NS = 2_000_000_000L;
    private static final int COLOR_FORMAT_YUV420_SEMIPLANAR = 21;
    private static final int COLOR_FORMAT_YUV420_FLEXIBLE = 0x7F420888;
    private static final int COLOR_FORMAT_QCOM_YUV420_SEMIPLANAR = 0x7FA30C00;

    private static MediaCodec codec;
    private static MediaMuxer muxer;
    private static int trackIndex = -1;
    private static boolean muxerStarted;
    private static int colorFormat;
    private static int frameWidth;
    private static int frameHeight;
    private static String lastError = "";
    private static String encoderName = "";
    private static long lastPresentationTimeUs;

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
            encoderName = "";
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
                Log.i(TAG, "Using encoder=" + encoder.getName() +
                        ", inputColorFormat=0x" +
                        Integer.toHexString(colorFormat) +
                        ", supported=" + describeColorFormats(
                                encoder.getCapabilitiesForType(MIME).colorFormats));
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
                encoderName = encoder.getName();
                codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
                codec.start();

                muxer = new MediaMuxer(
                        outputPath,
                        MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
                trackIndex = -1;
                muxerStarted = false;
                frameWidth = width;
                frameHeight = height;
                lastPresentationTimeUs = 0L;
                return true;
            } catch (Exception exception) {
                lastError = exception.toString();
                stopLocked();
                return false;
            }
        }
    }

    /** Queues a GPU-produced NV12 frame without an RGB-to-YUV CPU conversion. */
    public static boolean encodeYuv(byte[] nv12, long presentationTimeUs) {
        synchronized (LOCK) {
            if (codec == null || nv12 == null ||
                    nv12.length != frameWidth * frameHeight * 3 / 2) {
                lastError = "Invalid NV12 frame or encoder is stopped.";
                return false;
            }

            try {
                int inputIndex = codec.dequeueInputBuffer(0);
                if (inputIndex < 0) {
                    drainEncoder(false, 0L);
                    inputIndex = codec.dequeueInputBuffer(50_000);
                }
                if (inputIndex < 0) {
                    lastError = "H.264 encoder input queue is full.";
                    return false;
                }

                ByteBuffer input = codec.getInputBuffer(inputIndex);
                if (input == null || input.capacity() < nv12.length) {
                    lastError = "H.264 encoder input buffer is too small.";
                    return false;
                }
                input.clear();
                input.put(nv12);
                codec.queueInputBuffer(
                        inputIndex,
                        0,
                        nv12.length,
                        Math.max(0L, presentationTimeUs),
                        0);
                lastPresentationTimeUs = Math.max(
                        lastPresentationTimeUs,
                        presentationTimeUs);
                drainEncoder(false, 0L);
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

    public static String getEncoderName() {
        synchronized (LOCK) {
            return encoderName;
        }
    }

    private static void stopLocked() {
        if (codec == null && muxer == null) {
            trackIndex = -1;
            muxerStarted = false;
            return;
        }

        long inputDeadlineNs = System.nanoTime() + EOS_DRAIN_TIMEOUT_NS;
        try {
            if (codec != null) {
                int inputIndex = -1;
                while (inputIndex < 0 && System.nanoTime() < inputDeadlineNs) {
                    drainEncoder(false, inputDeadlineNs);
                    long remainingUs = Math.max(
                            0L,
                            (inputDeadlineNs - System.nanoTime()) / 1_000L);
                    inputIndex = codec.dequeueInputBuffer(
                            Math.min(10_000L, remainingUs));
                }
                if (inputIndex >= 0) {
                    codec.queueInputBuffer(
                            inputIndex,
                            0,
                            0,
                            lastPresentationTimeUs + 1L,
                            MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                    long drainDeadlineNs =
                            System.nanoTime() + EOS_DRAIN_TIMEOUT_NS;
                    if (!drainEncoder(true, drainDeadlineNs) &&
                            lastError.length() == 0) {
                        lastError = "H.264 encoder EOS drain timed out.";
                    }
                } else if (lastError.length() == 0) {
                    lastError = "H.264 encoder EOS input timed out.";
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
            trackIndex = -1;
            muxerStarted = false;
            encoderName = "";
            lastPresentationTimeUs = 0L;
        }
    }

    private static boolean drainEncoder(boolean endOfStream, long deadlineNs) {
        if (codec == null) {
            return false;
        }

        MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();
        while (true) {
            if (deadlineNs > 0L && System.nanoTime() >= deadlineNs) {
                return false;
            }
            long timeoutUs = endOfStream
                    ? Math.max(0L, Math.min(10_000L,
                            (deadlineNs - System.nanoTime()) / 1_000L))
                    : 0L;
            int outputIndex = codec.dequeueOutputBuffer(bufferInfo, timeoutUs);
            if (outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!endOfStream) {
                    return false;
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
                return true;
            }
        }
    }

    private static MediaCodecInfo findEncoder() {
        MediaCodecList list = new MediaCodecList(MediaCodecList.ALL_CODECS);
        MediaCodecInfo[] infos = list.getCodecInfos();
        // Prefer the Quest hardware AVC encoder. The first codec in ALL_CODECS
        // is not guaranteed to be hardware accelerated and can be a very slow
        // software implementation at 1920x1080.
        for (MediaCodecInfo info : infos) {
            if (isUsableEncoder(info) && isHardwareEncoder(info)) {
                return info;
            }
        }
        for (MediaCodecInfo info : infos) {
            if (isUsableEncoder(info)) {
                return info;
            }
        }
        return null;
    }

    private static boolean isUsableEncoder(MediaCodecInfo info) {
        if (info == null || !info.isEncoder()) {
            return false;
        }
        String[] types = info.getSupportedTypes();
        for (String type : types) {
            if (MIME.equalsIgnoreCase(type) &&
                    chooseColorFormat(info.getCapabilitiesForType(MIME).colorFormats) != 0) {
                return true;
            }
        }
        return false;
    }

    private static boolean isHardwareEncoder(MediaCodecInfo info) {
        if (Build.VERSION.SDK_INT >= 29) {
            return info.isHardwareAccelerated() && !info.isSoftwareOnly();
        }
        String name = info.getName().toLowerCase();
        return !name.startsWith("omx.google.") && !name.startsWith("c2.android.");
    }

    private static int chooseColorFormat(int[] formats) {
        for (int format : formats) {
            if (format == COLOR_FORMAT_YUV420_SEMIPLANAR) {
                return format;
            }
        }
        for (int format : formats) {
            if (format == COLOR_FORMAT_QCOM_YUV420_SEMIPLANAR) {
                return format;
            }
        }
        for (int format : formats) {
            if (format == COLOR_FORMAT_YUV420_FLEXIBLE) {
                return format;
            }
        }
        return 0;
    }

    private static String describeColorFormats(int[] formats) {
        StringBuilder builder = new StringBuilder("[");
        for (int index = 0; index < formats.length; index++) {
            if (index > 0) {
                builder.append(",");
            }
            builder.append("0x").append(Integer.toHexString(formats[index]));
        }
        return builder.append("]").toString();
    }

}
