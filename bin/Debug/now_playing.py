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
from rapidfuzz import process, fuzz

# ---------------- CONFIG ----------------
CLIENT_ID = "6e275fbc81f14f50a9e34de55c7417c0" 
REDIRECT_URI = "http://127.0.0.1:8888/callback"
SCOPES = "user-read-playback-state"
SESSION_FILE = "spotify_session.json"
LYRICS_FOLDER = "lyrics_cache"
LRCLIB_BASE = "https://lrclib.net"
NETWORK_RETRY_LIMIT = 3
AUTH_TIMEOUT = 60 

os.makedirs(LYRICS_FOLDER, exist_ok=True)
app = Flask(__name__)

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

# ---------------- HELPER FUNCTIONS ----------------
def hide_console_window():
    try:
        kernel32 = ctypes.WinDLL('kernel32')
        user32 = ctypes.WinDLL('user32')
        hWnd = kernel32.GetConsoleWindow()
        if hWnd: user32.ShowWindow(hWnd, 0)
    except: pass

def generate_pkce_pair():
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().replace("=", "")
    return verifier, challenge
CODE_VERIFIER, CODE_CHALLENGE = generate_pkce_pair()

def get_with_retries(url, params=None, headers=None):
    for _ in range(NETWORK_RETRY_LIMIT):
        try:
            return requests.get(url, params=params, headers=headers, timeout=5)
        except: time.sleep(0.5)
    return None

def post_with_retries(url, data):
    for _ in range(NETWORK_RETRY_LIMIT):
        try:
            return requests.post(url, data=data, timeout=5)
        except: time.sleep(0.5)
    return None

def swap_code_for_token(code):
    payload = {"client_id": CLIENT_ID, "grant_type": "authorization_code", "code": code, "redirect_uri": REDIRECT_URI, "code_verifier": CODE_VERIFIER}
    r = post_with_retries("https://accounts.spotify.com/api/token", payload)
    return r.json() if r else None

def refresh_token_request(refresh_token):
    payload = {"client_id": CLIENT_ID, "grant_type": "refresh_token", "refresh_token": refresh_token}
    r = post_with_retries("https://accounts.spotify.com/api/token", payload)
    return r.json() if r else None

def save_session(access_token, refresh_token):
    with open(SESSION_FILE, "w", encoding="utf8") as f:
        json.dump({"access_token": access_token, "refresh_token": refresh_token}, f)

def load_session():
    if not os.path.exists(SESSION_FILE): return None, None
    try:
        with open(SESSION_FILE, "r", encoding="utf8") as f:
            data = json.load(f)
            return data.get("access_token"), data.get("refresh_token")
    except: return None, None

def get_playback(access_token):
    r = get_with_retries("https://api.spotify.com/v1/me/player", headers={"Authorization": f"Bearer {access_token}"})
    if not r: return {"error": "network"}
    if r.status_code == 204: return None 
    if r.status_code == 200: return r.json()
    if r.status_code == 401: return {"error": "expired"}
    return {"error": f"status {r.status_code}"}

# ---------------- SMART LYRIC LOGIC ----------------

def clean_metadata(text):
    """
    Cleans Spotify titles to match lyric databases better.
    Ex: "Bohemian Rhapsody - Remastered 2011" -> "Bohemian Rhapsody"
    """
    if not text: return ""
    # 1. Remove text in brackets/parentheses like (feat. X) or [Live]
    text = re.sub(r"[\(\[].*?[\)\]]", "", text)
    # 2. Remove specific suffixes if they aren't in brackets
    text = re.sub(r"(?i)\s*-\s*remaster.*", "", text)
    text = re.sub(r"(?i)\s*-\s*stereo.*", "", text)
    text = re.sub(r"(?i)\s*-\s*mono.*", "", text)
    text = re.sub(r"(?i)\s*feat\..*", "", text)
    return text.strip()

def sanitize_filename(s):
    s = s.strip().replace("/", "-").replace("\\", "-")
    s = re.sub(r"[:<>\"|?*]", "", s)
    return re.sub(r"\s+", " ", s)

def lyrics_filepath_for(title, artist):
    # Always save using the RAW Spotify title so we find it next time without searching
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
        else: break
    return current_line

# ---------------- WORKER THREAD ----------------
class LyricWorker(threading.Thread):
    def __init__(self, title, artist, album):
        super().__init__(daemon=True)
        self.raw_title = title
        self.raw_artist = artist
        self.album = album

    def run(self):
        print(f"[Worker] Searching for: {self.raw_title} by {self.raw_artist}")
        
        # Strategy 1: Exact Match (Raw)
        got = self.fetch(self.raw_title, self.raw_artist)
        if got:
            self.save(got)
            return

        # Strategy 2: Clean Match (Crucial for Remastered/Feat tracks)
        clean_t = clean_metadata(self.raw_title)
        if clean_t != self.raw_title:
            print(f"[Worker] Exact failed. Trying clean: {clean_t}")
            got = self.fetch(clean_t, self.raw_artist)
            if got:
                self.save(got)
                return

        # Strategy 3: Aggressive Fuzzy Search (The logic from yesterday)
        # We define a list of search queries to cast a wide net
        queries = [
            f"{clean_t} {self.raw_artist}",
            f"{self.raw_artist} {clean_t}",
            clean_t,
            f"{self.raw_artist} {self.album or ''}"
        ]
        
        candidates = []
        seen_ids = set()

        for q in queries:
            try:
                # 'q' is the standard search param for lrclib.net
                r = requests.get(LRCLIB_BASE + "/api/search", params={"q": q}, timeout=5)
                if r.status_code == 200:
                    results = r.json()
                    if isinstance(results, list):
                        for rec in results:
                            if not isinstance(rec, dict): continue
                            # Avoid duplicates
                            rid = rec.get("id")
                            if rid and rid not in seen_ids:
                                seen_ids.add(rid)
                                candidates.append(rec)
            except Exception as e: 
                print(f"[Worker] Search error for '{q}': {e}")

        if not candidates:
            print("[Worker] No candidates found after aggressive search.")
            return

        # Fuzzy Match: Find the best candidate from the list
        choices = []
        mapping = {}
        for cand in candidates:
            ct = cand.get("trackName") or cand.get("name") or ""
            ca = cand.get("artistName") or cand.get("artist") or ""
            label = f"{ct} - {ca}"
            choices.append(label)
            mapping[label] = cand
        
        # We fuzzy match against our Clean Title + Artist
        target = f"{clean_t} - {self.raw_artist}"
        best = process.extractOne(target, choices, scorer=fuzz.WRatio, score_cutoff=50)
        
        if best:
            best_label, score, _ = best
            print(f"[Worker] Best fuzzy match: {best_label} (Score: {score})")
            best_cand = mapping.get(best_label)
            
            # Now fetch the details for this specific candidate to ensure we get synced lyrics
            # (Sometimes search results exclude full synced data)
            track_name = best_cand.get("trackName") or best_cand.get("name")
            artist_name = best_cand.get("artistName") or best_cand.get("artist")
            
            got = self.fetch(track_name, artist_name)
            if got:
                self.save(got)
            else:
                # If fetch fails, try using the candidate directly if it has lyrics
                if best_cand.get("syncedLyrics") or best_cand.get("lrc"):
                     self.save(best_cand)

    def fetch(self, t, a):
        try:
            r = requests.get(LRCLIB_BASE + "/api/get", params={"track_name": t, "artist_name": a}, timeout=5)
            return r.json() if r.status_code == 200 else None
        except: return None

    def save(self, data):
        try:
            final = convert_lrclib_to_target_json(data)
            path = lyrics_filepath_for(self.raw_title, self.raw_artist)
            with open(path, "w", encoding="utf8") as f:
                json.dump(final, f, ensure_ascii=False, indent=2)
            print(f"[Worker] Saved lyrics for {self.raw_title}")
        except Exception as e:
            print(f"[Worker] Error saving: {e}")

# ---------------- BACKGROUND LOOP ----------------
def background_loop():
    print("[BG] Starting...")
    access_token, refresh_token = load_session()

    if not access_token:
        # Initial Auth Flow
        auth_url = (f"https://accounts.spotify.com/authorize?client_id={CLIENT_ID}&response_type=code&redirect_uri={REDIRECT_URI}&scope={SCOPES}&code_challenge_method=S256&code_challenge={CODE_CHALLENGE}")
        webbrowser.open(auth_url)
        start_wait = time.time()
        while time.time() - start_wait < AUTH_TIMEOUT:
            if 'code' in auth_queue:
                token_data = swap_code_for_token(auth_queue['code'])
                if token_data and 'access_token' in token_data:
                    access_token = token_data['access_token']
                    refresh_token = token_data.get('refresh_token')
                    save_session(access_token, refresh_token)
                    break
            time.sleep(0.5)
        hide_console_window()
    else:
        hide_console_window()

    last_track_id = None
    active_lyrics = []

    while True:
        try:
            if not access_token: 
                time.sleep(2)
                continue

            data = get_playback(access_token)
            
            # Token Refresh
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

            # Change Detected?
            if track_id != last_track_id:
                last_track_id = track_id
                active_lyrics = []
                current_state["current_lyric"] = "Searching..."
                
                # Check Local Cache
                path = lyrics_filepath_for(current_state["track"], current_state["artist"])
                if os.path.exists(path):
                    try:
                        with open(path, "r", encoding="utf8") as f:
                            active_lyrics = json.load(f).get("timed_lyrics", [])
                    except: pass
                
                # If cache empty or missing, start searching
                if not active_lyrics:
                    LyricWorker(current_state["track"], current_state["artist"], item["album"]["name"]).start()

            # Hot-load lyrics if they just appeared from the worker
            if not active_lyrics:
                 path = lyrics_filepath_for(current_state["track"], current_state["artist"])
                 if os.path.exists(path):
                    try:
                        with open(path, "r", encoding="utf8") as f:
                            active_lyrics = json.load(f).get("timed_lyrics", [])
                    except: pass

            if active_lyrics:
                # 0.2s offset helps sync with bluetooth speakers/latency
                seconds = (progress_ms / 1000.0) + 0.2 
                txt = get_current_line(active_lyrics, seconds)
                current_state["current_lyric"] = txt if txt else "..."
            else:
                # Fallback: Just show the artist and track
                current_state["current_lyric"] = f"{current_state['artist']} - {current_state['track']}"

            time.sleep(0.1)

        except Exception: time.sleep(1)

@app.route('/callback')
def callback():
    code = request.args.get("code")
    if code:
        auth_queue['code'] = code
        return "<h1>Success! You can close this.</h1>"
    return "Error."

@app.route('/status')
def status():
    return jsonify(current_state)

if __name__ == "__main__":
    t = threading.Thread(target=background_loop, daemon=True)
    t.start()
    app.run(port=8888, debug=False, use_reloader=False)