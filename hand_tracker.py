#!/usr/bin/env python
import os
import time
import socket
import cv2
import urllib.request
import sys
import mediapipe as mp

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
options = HandLandmarkerOptions(
    base_options=base_options,
    num_hands=2,  # Track both hands
    running_mode=vision.RunningMode.VIDEO
)

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

            # Mirror the frame horizontally so movements feel natural
            frame_bgr = cv2.flip(frame_bgr, 1)
            
            # Convert BGR -> RGB
            frame_rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)

            # Create MediaPipe Image from numpy array
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=frame_rgb)
            
            # Detect hand landmarks
            timestamp_ms = int(time.time() * 1000)
            result = landmarker.detect_for_video(mp_image, timestamp_ms)

            # Extract hand centers and send by UDP
            # Format: "x1 y1 x2 y2" where -1 -1 means no hand
            hand_landmarks = getattr(result, "hand_landmarks", None)
            h, w = frame_bgr.shape[:2]
            
            # Initialize with no hands detected
            hand1_coords = (-1.0, -1.0)
            hand2_coords = (-1.0, -1.0)
            
            if hand_landmarks and len(hand_landmarks) > 0:
                # Process each detected hand
                for hand_idx, lm_list in enumerate(hand_landmarks):
                    # Calculate center of this hand
                    hand_cx = sum(lm.x for lm in lm_list) / len(lm_list)
                    hand_cy = sum(lm.y for lm in lm_list) / len(lm_list)
                    
                    # Store coordinates
                    if hand_idx == 0:
                        hand1_coords = (hand_cx, hand_cy)
                    elif hand_idx == 1:
                        hand2_coords = (hand_cx, hand_cy)
                    
                    # Draw landmarks for this hand (different colors)
                    color = (0, 255, 0) if hand_idx == 0 else (255, 0, 255)  # Green/Magenta
                    for lm in lm_list:
                        px, py = int(lm.x * w), int(lm.y * h)
                        cv2.circle(frame_bgr, (px, py), 3, color, -1)
                    
                    # Draw spotlight circle for this hand
                    hcx, hcy = int(hand_cx * w), int(hand_cy * h)
                    cv2.circle(frame_bgr, (hcx, hcy), 80, color, 2)
                    label = "HAND 1" if hand_idx == 0 else "HAND 2"
                    cv2.putText(frame_bgr, f"{label} ({hand_cx:.2f}, {hand_cy:.2f})", 
                               (hcx + 15, hcy), cv2.FONT_HERSHEY_SIMPLEX, 
                               0.5, color, 2)
            
            # Build message: "x1 y1 x2 y2"
            msg = f"{hand1_coords[0]:.6f} {hand1_coords[1]:.6f} {hand2_coords[0]:.6f} {hand2_coords[1]:.6f}"
            
            # Debug output
            if hand1_coords[0] >= 0 and hand2_coords[0] >= 0:
                print(f"Both hands: Hand1({hand1_coords[0]:.3f},{hand1_coords[1]:.3f}) Hand2({hand2_coords[0]:.3f},{hand2_coords[1]:.3f})")
            elif hand1_coords[0] >= 0:
                print(f"Hand 1 only at: ({hand1_coords[0]:.3f}, {hand1_coords[1]:.3f})")
            elif hand2_coords[0] >= 0:
                print(f"Hand 2 only at: ({hand2_coords[0]:.3f}, {hand2_coords[1]:.3f})")
            else:
                print("No hands detected")

            sock.sendto(msg.encode("utf-8"), (UDP_IP, UDP_PORT))

            # debug preview
            cv2.imshow("hand-tracker", frame_bgr)
            if cv2.waitKey(1) & 0xFF == 27:  # ESC
                print("ESC pressed, exiting.")
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()