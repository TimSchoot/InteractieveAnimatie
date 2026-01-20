#!/usr/bin/env python
import os
import time
import socket
import cv2
import urllib.request
import sys

from mediapipe.tasks.python import vision
try:
    from mediapipe.tasks.python.vision import HandLandmarker, HandLandmarkerOptions
except Exception:
    raise ImportError("HandLandmarker not found in mediapipe.tasks.python.vision")

try:
    from mediapipe.tasks.python.core import BaseOptions
except Exception:
    from mediapipe.tasks.python.core.base_options import BaseOptions

# UDP destination (WPF listens on localhost:5005)
UDP_IP = "127.0.0.1"
UDP_PORT = 5005
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# Model download
MODEL_FILENAME = "hand_landmarker.task"
MODEL_URL = "https://storage.googleapis.com/mediapipe-assets/hand_landmarker.task"

if not os.path.exists(MODEL_FILENAME):
    print(f"Downloading {MODEL_FILENAME} ...")
    urllib.request.urlretrieve(MODEL_URL, MODEL_FILENAME)
    print("Download complete.")

# Create hand landmarker options
base_options = BaseOptions(model_asset_path=MODEL_FILENAME)
options = HandLandmarkerOptions(base_options=base_options, num_hands=1)

# Camera helper: try to open a working camera index and print diagnostics
def open_camera_with_probe(max_index=4, prefer_index=0):
    # First try the preferred index
    print(f"Attempting to open camera index {prefer_index}...")
    cap = cv2.VideoCapture(prefer_index, cv2.CAP_DSHOW)
    if cap.isOpened():
        print(f"Camera opened at index {prefer_index}")
        return cap, prefer_index
    else:
        cap.release()
    # Probe other indices
    for i in range(max_index + 1):
        if i == prefer_index:
            continue
        print(f"Probing camera index {i}...")
        cap = cv2.VideoCapture(i, cv2.CAP_DSHOW)
        if cap.isOpened():
            print(f"Camera opened at index {i}")
            return cap, i
        cap.release()
    return None, None

# Run
with HandLandmarker.create_from_options(options) as landmarker:
    # Try to open camera
    cap, used_index = open_camera_with_probe(max_index=6, prefer_index=0)
    if cap is None:
        print("ERROR: Could not open any camera index (tried 0..6).")
        print(" - Close other apps that might use the camera (Teams/Zoom/Edge).")
        print(" - Check Windows Camera privacy settings: Settings > Privacy & security > Camera.")
        print(" - If you have multiple cameras, plug/unplug and retry or try different indices.")
        sys.exit(1)

    print(f"Using camera index {used_index}. Press ESC in the preview window to quit.")
    try:
        while True:
            ret, frame_bgr = cap.read()
            if not ret:
                print("Warning: frame read failed (camera disconnected or in use).")
                time.sleep(0.5)
                continue

            # Convert BGR -> RGB
            frame_rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)

            # Use the Tasks API image factory if available:
            # prefer vision.TensorImage then vision.Image
            mp_image = None
            if hasattr(vision, "TensorImage"):
                TensorImage = getattr(vision, "TensorImage")
                if hasattr(TensorImage, "create_from_rgb_image"):
                    mp_image = TensorImage.create_from_rgb_image(frame_rgb)
            if mp_image is None and hasattr(vision, "Image"):
                ImageFactory = getattr(vision, "Image")
                if hasattr(ImageFactory, "create_from_rgb_image"):
                    mp_image = ImageFactory.create_from_rgb_image(frame_rgb)
            if mp_image is None:
                # Last resort: use detect() on raw numpy if your build supports it (may fail)
                print("WARNING: Could not create Tasks Image/TensorImage; attempting fallback.")
                try:
                    result = landmarker.detect(frame_rgb)
                except Exception as ex:
                    print("Fatal: fallback detect failed:", ex)
                    break
            else:
                timestamp_ms = int(time.time() * 1000)
                result = landmarker.detect_for_video(mp_image, timestamp_ms)

            # Extract normalized center and send by UDP
            hand_landmarks = getattr(result, "hand_landmarks", None)
            if hand_landmarks:
                lm_list = hand_landmarks[0].landmarks
                cx = sum(lm.x for lm in lm_list) / len(lm_list)
                cy = sum(lm.y for lm in lm_list) / len(lm_list)
                msg = f"{cx:.6f} {cy:.6f}"
            else:
                msg = "-1 -1"

            sock.sendto(msg.encode("utf-8"), (UDP_IP, UDP_PORT))

            # debug preview
            cv2.imshow("hand-tracker", frame_bgr)
            if cv2.waitKey(1) & 0xFF == 27:  # ESC
                print("ESC pressed, exiting.")
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()

    import cv2
    cap = cv2.VideoCapture(0, cv2.CAP_DSHOW)
    print("isOpened:", cap.isOpened())
    cap.release()