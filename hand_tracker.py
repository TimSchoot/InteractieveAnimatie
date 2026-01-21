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
    from mediapipe.tasks.python.vision import GestureRecognizer, GestureRecognizerOptions
except Exception:
    raise ImportError("HandLandmarker or GestureRecognizer not found in mediapipe.tasks.python.vision")

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

GESTURE_MODEL_FILENAME = "gesture_recognizer.task"
GESTURE_MODEL_URL = "https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task"

if not os.path.exists(MODEL_FILENAME):
    print(f"Downloading {MODEL_FILENAME} ...")
    urllib.request.urlretrieve(MODEL_URL, MODEL_FILENAME)
    print("Download complete.")

if not os.path.exists(GESTURE_MODEL_FILENAME):
    print(f"Downloading {GESTURE_MODEL_FILENAME} ...")
    urllib.request.urlretrieve(GESTURE_MODEL_URL, GESTURE_MODEL_FILENAME)
    print("Download complete.")

# Create hand landmarker options
base_options = BaseOptions(model_asset_path=MODEL_FILENAME)
options = HandLandmarkerOptions(
    base_options=base_options,
    num_hands=2,  # Track both hands
    running_mode=vision.RunningMode.VIDEO,
    min_hand_detection_confidence=0.3,
    min_hand_presence_confidence=0.3,
    min_tracking_confidence=0.3
)

# Create gesture recognizer options with lower confidence for better long-range detection
gesture_base_options = BaseOptions(model_asset_path=GESTURE_MODEL_FILENAME)
gesture_options = GestureRecognizerOptions(
    base_options=gesture_base_options,
    num_hands=2,
    running_mode=vision.RunningMode.VIDEO,
    min_hand_detection_confidence=0.3,  # Lower = better at distance (default: 0.5)
    min_hand_presence_confidence=0.3,   # Lower = better at distance (default: 0.5)
    min_tracking_confidence=0.3         # Lower = smoother tracking (default: 0.5)
)

# Camera helper: try to open a working camera index and print diagnostics
def open_camera_with_probe(max_index=4, prefer_index=0):
    # First try the preferred index
    print(f"Attempting to open camera index {prefer_index}...")
    cap = cv2.VideoCapture(prefer_index, cv2.CAP_DSHOW)
    if cap.isOpened():
        # Set higher resolution for better long-range detection
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
        # Try to disable auto-exposure for more consistent detection
        cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, 0.25)
        print(f"Camera opened at index {prefer_index}")
        # Print actual resolution
        w = cap.get(cv2.CAP_PROP_FRAME_WIDTH)
        h = cap.get(cv2.CAP_PROP_FRAME_HEIGHT)
        print(f"Camera resolution: {int(w)}x{int(h)}")
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
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
            cap.set(cv2.CAP_PROP_AUTO_EXPOSURE, 0.25)
            print(f"Camera opened at index {i}")
            w = cap.get(cv2.CAP_PROP_FRAME_WIDTH)
            h = cap.get(cv2.CAP_PROP_FRAME_HEIGHT)
            print(f"Camera resolution: {int(w)}x{int(h)}")
            return cap, i
        cap.release()
    return None, None

# Run
with HandLandmarker.create_from_options(options) as landmarker, \
     GestureRecognizer.create_from_options(gesture_options) as gesture_recognizer:
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

            # Flip horizontally for mirror effect (more intuitive)
            frame_bgr = cv2.flip(frame_bgr, 1)
            
            # Convert BGR -> RGB
            frame_rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)

            # Create MediaPipe Image from numpy array
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=frame_rgb)
            
            # Detect hand landmarks
            timestamp_ms = int(time.time() * 1000)
            result = landmarker.detect_for_video(mp_image, timestamp_ms)
            
            # Detect gestures
            gesture_result = gesture_recognizer.recognize_for_video(mp_image, timestamp_ms)

            # Extract hand centers and send by UDP
            # Format: "x1 y1 x2 y2 gesture_id" where -1 -1 means no hand
            # gesture_id: 0=none, 1=fist, 2=victory, 3=thumb_up, 4=thumb_down, 5=open_palm, 6=pointing_up, 7=middle_finger
            hand_landmarks = getattr(result, "hand_landmarks", None)
            gestures = getattr(gesture_result, "gestures", None)
            h, w = frame_bgr.shape[:2]
            
            # Initialize with no hands detected
            hand1_coords = (-1.0, -1.0)
            hand2_coords = (-1.0, -1.0)
            gesture_id = 0  # 0 = no gesture recognized
            gesture_display = ""
            
            if hand_landmarks and len(hand_landmarks) > 0:
                # Check for gestures using MediaPipe's gesture recognizer
                if gestures and len(gestures) > 0:
                    # Check first hand for gestures
                    first_gesture = gestures[0]
                    if len(first_gesture) > 0:
                        gesture_name = first_gesture[0].category_name
                        gesture_score = first_gesture[0].score
                        
                        # Lower confidence threshold for better long-range detection
                        # MediaPipe Studio uses ~0.5, we use 0.5 too
                        if gesture_score > 0.5:
                            gesture_display = f"{gesture_name} ({gesture_score:.2f})"
                            if gesture_name == "Closed_Fist":
                                gesture_id = 1
                            elif gesture_name == "Victory":
                                gesture_id = 2
                            elif gesture_name == "Thumb_Up":
                                gesture_id = 3
                            elif gesture_name == "Thumb_Down":
                                gesture_id = 4
                            elif gesture_name == "Open_Palm":
                                gesture_id = 5
                            elif gesture_name == "Pointing_Up":
                                gesture_id = 6
                
                # Custom middle finger detection
                if gesture_id == 0 and hand_landmarks and len(hand_landmarks) > 0:
                    lm_list = hand_landmarks[0]
                    # Landmark indices: thumb_tip=4, index_tip=8, middle_tip=12, ring_tip=16, pinky_tip=20, wrist=0
                    thumb_tip = lm_list[4]
                    index_tip = lm_list[8]
                    middle_tip = lm_list[12]
                    ring_tip = lm_list[16]
                    pinky_tip = lm_list[20]
                    wrist = lm_list[0]
                    middle_mcp = lm_list[9]  # Middle finger base
                    
                    def distance(p1, p2):
                        return ((p1.x - p2.x)**2 + (p1.y - p2.y)**2)**0.5
                    
                    # Check if middle finger is extended
                    middle_extended = distance(middle_tip, wrist) > distance(middle_mcp, wrist) * 1.5
                    
                    # Check if other fingers are curled (close to palm)
                    index_curled = distance(index_tip, wrist) < distance(middle_tip, wrist) * 0.7
                    ring_curled = distance(ring_tip, wrist) < distance(middle_tip, wrist) * 0.7
                    pinky_curled = distance(pinky_tip, wrist) < distance(middle_tip, wrist) * 0.7
                    
                    # Middle finger gesture: middle extended, others curled
                    if middle_extended and index_curled and ring_curled and pinky_curled:
                        gesture_id = 7
                        gesture_display = "Middle_Finger ?? (1.00)"
                
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
                    
                    # Add gesture info for hand 1
                    if hand_idx == 0 and gesture_display:
                        label += f" - {gesture_display}"
                        # Draw colored circle based on gesture
                        if gesture_id == 1:  # Fist
                            cv2.circle(frame_bgr, (hcx, hcy), 100, (0, 0, 255), 3)  # Red
                        elif gesture_id == 2:  # Victory/Peace
                            cv2.circle(frame_bgr, (hcx, hcy), 100, (255, 255, 0), 3)  # Cyan
                        elif gesture_id == 3:  # Thumb up
                            cv2.circle(frame_bgr, (hcx, hcy), 100, (0, 255, 255), 3)  # Yellow
                        elif gesture_id == 4:  # Thumb down
                            cv2.circle(frame_bgr, (hcx, hcy), 100, (255, 0, 0), 3)  # Blue
                        elif gesture_id == 7:  # Middle finger
                            cv2.circle(frame_bgr, (hcx, hcy), 100, (128, 0, 128), 3)  # Purple
                    
                    cv2.putText(frame_bgr, f"{label} ({hand_cx:.2f}, {hand_cy:.2f})", 
                               (hcx + 15, hcy), cv2.FONT_HERSHEY_SIMPLEX, 
                               0.5, color, 2)
            
            # Build message: "x1 y1 x2 y2 gesture_id"
            msg = f"{hand1_coords[0]:.6f} {hand1_coords[1]:.6f} {hand2_coords[0]:.6f} {hand2_coords[1]:.6f} {gesture_id}"
            
            # Debug output
            gesture_str = f" [{gesture_display}]" if gesture_display else ""
            if hand1_coords[0] >= 0 and hand2_coords[0] >= 0:
                print(f"Both hands: Hand1({hand1_coords[0]:.3f},{hand1_coords[1]:.3f}) Hand2({hand2_coords[0]:.3f},{hand2_coords[1]:.3f}){gesture_str}")
            elif hand1_coords[0] >= 0:
                print(f"Hand 1 only at: ({hand1_coords[0]:.3f}, {hand1_coords[1]:.3f}){gesture_str}")
            elif hand2_coords[0] >= 0:
                print(f"Hand 2 only at: ({hand2_coords[0]:.3f}, {hand2_coords[1]:.3f})")
            else:
                print("No hands detected")
            
            # Send coordinates via UDP
            sock.sendto(msg.encode(), (UDP_IP, UDP_PORT))

            # Show preview
            cv2.imshow("Hand Tracking Preview (press ESC to quit)", frame_bgr)
            if cv2.waitKey(1) & 0xFF == 27:  # ESC key
                break

    except KeyboardInterrupt:
        print("\nKeyboard interrupt received, exiting...")
    finally:
        cap.release()
        cv2.destroyAllWindows()
        print("Camera released and windows closed.")
