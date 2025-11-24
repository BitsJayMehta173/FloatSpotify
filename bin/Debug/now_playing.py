#!/usr/bin/env python3

import os
import re
import time
import json
import base64
import hashlib
import secrets
import threading
import webbrowser
import requests
import ctypes
from flask import Flask, request, jsonify

# ---------------- CONFIG ----------------
CLIENT_ID = "6e275fbc81f14f50a9e34de55c7417c0" 
REDIRECT_URI = "http://127.0.0.1:8888/callback"
SCOPES = "user-read-playback-state"
SESSION_FILE = "spotify_session.json"
LYRICS_FOLDER = "lyrics_cache"
LRCLIB_BASE = "https://lrclib.net"
NETWORK_RETRY_LIMIT = 5
AUTH_TIMEOUT = 60 # Seconds

os.makedirs(LYRICS_FOLDER, exist_ok=True)

# ---------------- WINDOW MANAGEMENT (NEW) ----------------
def hide_console_window():
    """Hides the console window but keeps the process running."""
    try:
        # Get handle to the console window
        kernel32 = ctypes.WinDLL('kernel32')
        user32 = ctypes.WinDLL('user32')
        hWnd = kernel32.GetConsoleWindow()
        
        if hWnd:
            # SW_HIDE = 0
            user32.ShowWindow(hWnd, 0)
            print("[SYSTEM] Console hidden. Server running in background.")
    except Exception as e:
        print(f"[SYSTEM] Could not hide window: {e}")

# ---------------- GLOBAL STATE ----------------
current_state = {
    "is_playing": False,
    "track": "",
    "artist": "",
    "album": "",
    "current_lyric": "Waiting for Spotify...",
    "progress_ms": 0,
    "auth_status": "WAITING" 
}
auth_queue = {}

# ---------------- FLASK APP ----------------
app = Flask(__name__)

# ---------------- HELPER FUNCTIONS ----------------

def generate_pkce_pair():
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().replace("=", "")
    return verifier, challenge

CODE_VERIFIER, CODE_CHALLENGE = generate_pkce_pair()

def get_with_retries(url, params=None, headers=None, timeout=6, retries=NETWORK_RETRY_LIMIT):
    for attempt in range(retries):
        try:
            r = requests.get(url, params=params, headers=headers, timeout=timeout)
            return r
        except Exception:
            if attempt + 1 == retries: return None
            time.sleep(1)
    return None

def post_with_retries(url, data, timeout=6, retries=NETWORK_RETRY_LIMIT):
    for attempt in range(retries):
        try:
            r = requests.post(url, data=data, timeout=timeout)
            return r
        except Exception:
            if attempt + 1 == retries: return None
            time.sleep(1)
    return None

def swap_code_for_token(code):
    payload = {
        "client_id": CLIENT_ID,
        "grant_type": "authorization_code",
        "code": code,
        "redirect_uri": REDIRECT_URI,
        "code_verifier": CODE_VERIFIER,
    }
    r = post_with_retries("https://accounts.spotify.com/api/token", payload)
    return r.json() if r else None

def refresh_token_request(refresh_token):
    payload = {
        "client_id": CLIENT_ID,
        "grant_type": "refresh_token",
        "refresh_token": refresh_token,
    }
    r = post_with_retries("https://accounts.spotify.com/api/token", payload)
    return r.json() if r else None

def save_session(access_token, refresh_token):
    with open(SESSION_FILE, "w", encoding="utf8") as f:
        json.dump({"access_token": access_token, "refresh_token": refresh_token, "saved_at": time.time()}, f)

def load_session():
    if not os.path.exists(SESSION_FILE): return None, None
    try:
        with open(SESSION_FILE, "r", encoding="utf8") as f:
            data = json.load(f)
            return data.get("access_token"), data.get("refresh_token")
    except: return None, None

def get_playback(access_token):
    url = "https://api.spotify.com/v1/me/player"
    headers = {"Authorization": f"Bearer {access_token}"}
    r = get_with_retries(url, headers=headers)
    if not r: return {"error": "network"}
    if r.status_code == 204: return None 
    if r.status_code == 200: return r.json()
    if r.status_code == 401: return {"error": "expired"}
    return {"error": f"status {r.status_code}"}

# ---------------- LYRIC LOGIC ----------------
def sanitize_filename(s):
    s = s.strip().replace("/", "-").replace("\\", "-")
    s = re.sub(r"[:<>\"|?*]", "", s)
    return re.sub(r"\s+", " ", s)

def lyrics_filepath_for(title, artist):
    fname = sanitize_filename(f"{artist} - {title}") + ".json"
    return os.path.join(LYRICS_FOLDER, fname)

SYNCED_LINE_RE = re.compile(r"\[?(\d{2}):(\d{2}(?:\.\d+)?)\]?")
def convert_lrclib_to_target_json(got_json):
    synced_raw = got_json.get("syncedLyrics") or got_json.get("lrc")
    timed = []
    if isinstance(synced_raw, str) and synced_raw.strip():
        for ln in synced_raw.splitlines():
            m = SYNCED_LINE_RE.search(ln)
            if not m: continue
            mm = int(m.group(1))
            ss = float(m.group(2))
            seconds = mm * 60 + ss
            text = ln[m.end():].strip()
            timed.append({"seconds": seconds, "line": text})
    return {"timed_lyrics": timed}

def get_current_line(timed_lyrics, current_time_seconds):
    if not timed_lyrics: return ""
    current_line = ""
    for entry in timed_lyrics:
        if entry["seconds"] <= current_time_seconds:
            current_line = entry["line"]
        else:
            break
    return current_line

# ---------------- WORKER THREAD ----------------
class LyricWorker(threading.Thread):
    def __init__(self, title, artist, album):
        super().__init__(daemon=True)
        self.title = title
        self.artist = artist
        self.album = album

    def run(self):
        try:
            r = requests.get(LRCLIB_BASE + "/api/get", params={"track_name": self.title, "artist_name": self.artist}, timeout=5)
            if r.status_code == 200:
                final = convert_lrclib_to_target_json(r.json())
                path = lyrics_filepath_for(self.title, self.artist)
                with open(path, "w", encoding="utf8") as f:
                    json.dump(final, f)
        except: pass

# ---------------- BACKGROUND LOOP ----------------
def background_loop():
    print("[BG] Starting...")
    access_token, refresh_token = load_session()

    # --- AUTH CHECK WITH TIMEOUT ---
    if not access_token:
        print("[BG] No session found. Opening browser for authorization...")
        current_state["auth_status"] = "MISSING"
        
        auth_url = (f"https://accounts.spotify.com/authorize?client_id={CLIENT_ID}"
                    f"&response_type=code&redirect_uri={REDIRECT_URI}&scope={SCOPES}"
                    f"&code_challenge_method=S256&code_challenge={CODE_CHALLENGE}")
        webbrowser.open(auth_url)
        
        # WAIT FOR 60 SECONDS
        start_wait = time.time()
        auth_success = False
        
        print(f"[BG] You have {AUTH_TIMEOUT} seconds to authorize...")
        
        while time.time() - start_wait < AUTH_TIMEOUT:
            if 'code' in auth_queue:
                token_data = swap_code_for_token(auth_queue['code'])
                if token_data and 'access_token' in token_data:
                    access_token = token_data['access_token']
                    refresh_token = token_data.get('refresh_token')
                    save_session(access_token, refresh_token)
                    current_state["auth_status"] = "OK"
                    auth_success = True
                    print("[BG] Authorization Successful!")
                    break
            time.sleep(0.5)
        
        if not auth_success:
            print("[BG] Authorization timed out. Continuing without sync.")
            current_state["auth_status"] = "TIMEOUT"
            # We hide the console anyway so the user sees the dashboard
            hide_console_window()
        else:
            # Success! Hide the console now.
            time.sleep(1) # Small delay to read "Success"
            hide_console_window()

    else:
        # Session already exists, hide immediately
        print("[BG] Session valid. Hiding console...")
        hide_console_window()

    # --- MAIN POLLING ---
    last_track_id = None
    active_lyrics = []

    while True:
        try:
            if not access_token:
                time.sleep(2)
                continue

            data = get_playback(access_token)
            
            if isinstance(data, dict) and data.get("error") == "expired":
                new_tok = refresh_token_request(refresh_token)
                if new_tok and 'access_token' in new_tok:
                    access_token = new_tok['access_token']
                    if 'refresh_token' in new_tok: refresh_token = new_tok['refresh_token']
                    save_session(access_token, refresh_token)
                continue

            if not data or not data.get("item"):
                current_state["is_playing"] = False
                current_state["current_lyric"] = "Spotify Paused"
                time.sleep(1)
                continue

            item = data["item"]
            track_id = item["id"]
            progress_ms = data["progress_ms"]
            
            current_state["is_playing"] = data["is_playing"]
            current_state["track"] = item["name"]
            current_state["artist"] = item["artists"][0]["name"]
            current_state["album"] = item["album"]["name"]

            if track_id != last_track_id:
                last_track_id = track_id
                active_lyrics = []
                current_state["current_lyric"] = "Loading..."
                path = lyrics_filepath_for(current_state["track"], current_state["artist"])
                if os.path.exists(path):
                    try:
                        with open(path, "r", encoding="utf8") as f:
                            active_lyrics = json.load(f).get("timed_lyrics", [])
                    except: pass
                else:
                    LyricWorker(current_state["track"], current_state["artist"], current_state["album"]).start()
            
            if not active_lyrics:
                 path = lyrics_filepath_for(current_state["track"], current_state["artist"])
                 if os.path.exists(path):
                    try:
                        with open(path, "r", encoding="utf8") as f:
                            active_lyrics = json.load(f).get("timed_lyrics", [])
                    except: pass

            if active_lyrics:
                seconds = (progress_ms / 1000.0) + 0.2 
                txt = get_current_line(active_lyrics, seconds)
                current_state["current_lyric"] = txt if txt else "..."
            else:
                current_state["current_lyric"] = f"{current_state['artist']} - {current_state['track']}"

            time.sleep(0.15)

        except Exception:
            time.sleep(1)

# ---------------- WEB SERVER ----------------
@app.route('/callback')
def callback():
    code = request.args.get("code")
    if code:
        auth_queue['code'] = code
        return "<h1>Success! You can close this tab.</h1>"
    return "Error: No code returned."

@app.route('/status')
def status():
    return jsonify(current_state)

if __name__ == "__main__":
    t = threading.Thread(target=background_loop, daemon=True)
    t.start()
    app.run(port=8888, debug=False, use_reloader=False)