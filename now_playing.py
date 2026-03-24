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
import signal
import sys
from flask import Flask, request, jsonify

# ---------------- SHUTDOWN SUPPORT ----------------

def shutdown_handler(signum, frame):
    print("Python backend shutting down...")
    os._exit(0)

signal.signal(signal.SIGTERM, shutdown_handler)
signal.signal(signal.SIGINT, shutdown_handler)

# ---------------- PATH SETUP ----------------

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SESSION_FILE = os.path.join(BASE_DIR, "spotify_session.json")
LYRICS_FOLDER = os.path.join(BASE_DIR, "lyrics_cache")

os.makedirs(LYRICS_FOLDER, exist_ok=True)

# ---------------- CONFIG ----------------

CLIENT_ID = "6e275fbc81f14f50a9e34de55c7417c0"
REDIRECT_URI = "http://127.0.0.1:8888/callback"
SCOPES = "user-read-playback-state"

LRCLIB_BASE = "https://lrclib.net"

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

# ---------------- PKCE ----------------

def generate_pkce_pair():
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(
        hashlib.sha256(verifier.encode()).digest()
    ).decode().replace("=", "")
    return verifier, challenge


CODE_VERIFIER, CODE_CHALLENGE = generate_pkce_pair()

# ---------------- SESSION ----------------

def save_session(access_token, refresh_token):

    with open(SESSION_FILE, "w") as f:
        json.dump(
            {
                "access_token": access_token,
                "refresh_token": refresh_token
            },
            f
        )


def load_session():

    if not os.path.exists(SESSION_FILE):
        return None, None

    with open(SESSION_FILE) as f:
        data = json.load(f)

    return data.get("access_token"), data.get("refresh_token")

# ---------------- SPOTIFY AUTH ----------------

def swap_code_for_token(code):

    payload = {
        "client_id": CLIENT_ID,
        "grant_type": "authorization_code",
        "code": code,
        "redirect_uri": REDIRECT_URI,
        "code_verifier": CODE_VERIFIER
    }

    r = requests.post("https://accounts.spotify.com/api/token", data=payload)

    return r.json()

# ---------------- SPOTIFY PLAYBACK ----------------

def get_playback(access_token):

    r = requests.get(
        "https://api.spotify.com/v1/me/player/currently-playing",
        headers={"Authorization": f"Bearer {access_token}"}
    )

    if r.status_code == 204:
        return None

    if r.status_code == 200:
        return r.json()

    return None

# ---------------- LYRICS ----------------

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

    if synced_raw:

        for ln in synced_raw.splitlines():

            m = SYNCED_LINE_RE.search(ln)

            if not m:
                continue

            mm = int(m.group(1))
            ss = float(m.group(2))

            seconds = mm * 60 + ss

            text = ln[m.end():].strip()

            timed.append({"seconds": seconds, "line": text})

    return {"timed_lyrics": timed}


def get_current_line(timed_lyrics, current_time_seconds):

    current_line = ""

    for entry in timed_lyrics:

        if entry["seconds"] <= current_time_seconds:
            current_line = entry["line"]
        else:
            break

    return current_line

# ---------------- LYRIC WORKER ----------------

class LyricWorker(threading.Thread):

    def __init__(self, title, artist):

        super().__init__(daemon=True)

        self.title = title
        self.artist = artist

    def run(self):

        try:

            r = requests.get(
                LRCLIB_BASE + "/api/get",
                params={
                    "track_name": self.title,
                    "artist_name": self.artist
                }
            )

            if r.status_code != 200:
                return

            final = convert_lrclib_to_target_json(r.json())

            path = lyrics_filepath_for(self.title, self.artist)

            with open(path, "w", encoding="utf8") as f:
                json.dump(final, f, indent=2, ensure_ascii=False)

        except:
            pass

# ---------------- BACKGROUND LOOP ----------------

def background_loop():

    access_token, refresh_token = load_session()

    if access_token:
        current_state["auth_status"] = "AUTHORIZED"

    if not access_token:

        auth_url = (
            "https://accounts.spotify.com/authorize"
            f"?client_id={CLIENT_ID}"
            "&response_type=code"
            f"&redirect_uri={REDIRECT_URI}"
            f"&scope={SCOPES}"
            "&code_challenge_method=S256"
            f"&code_challenge={CODE_CHALLENGE}"
        )

        webbrowser.open(auth_url)

        while "code" not in auth_queue:
            time.sleep(0.5)

        token_data = swap_code_for_token(auth_queue["code"])

        access_token = token_data["access_token"]
        refresh_token = token_data.get("refresh_token")

        save_session(access_token, refresh_token)

        current_state["auth_status"] = "AUTHORIZED"

    last_track_id = None
    active_lyrics = []

    last_progress_ms = 0
    last_progress_time = time.time()

    while True:

        data = get_playback(access_token)

        if not data or not data.get("item"):

            current_state["is_playing"] = False
            current_state["current_lyric"] = "Spotify Paused"

            time.sleep(1)
            continue

        item = data["item"]

        track_id = item["id"]
        progress_ms = data["progress_ms"]

        current_state["track"] = item["name"]
        current_state["artist"] = item["artists"][0]["name"]
        current_state["album"] = item["album"]["name"]
        current_state["is_playing"] = data["is_playing"]

        last_progress_ms = progress_ms
        last_progress_time = time.time()

        if track_id != last_track_id:

            last_track_id = track_id
            active_lyrics = []

            path = lyrics_filepath_for(
                current_state["track"],
                current_state["artist"]
            )

            if os.path.exists(path):

                with open(path) as f:
                    active_lyrics = json.load(f)["timed_lyrics"]

            else:

                LyricWorker(
                    current_state["track"],
                    current_state["artist"]
                ).start()

        if not active_lyrics:

            path = lyrics_filepath_for(
                current_state["track"],
                current_state["artist"]
            )

            if os.path.exists(path):

                with open(path) as f:
                    active_lyrics = json.load(f)["timed_lyrics"]

        if current_state["is_playing"]:
            elapsed = (time.time() - last_progress_time) * 1000
            progress_estimate = last_progress_ms + elapsed
        else:
            progress_estimate = last_progress_ms

        current_state["progress_ms"] = int(progress_estimate)

        seconds = progress_estimate / 1000

        if active_lyrics:

            txt = get_current_line(active_lyrics, seconds)

            current_state["current_lyric"] = txt if txt else "..."

        else:

            current_state["current_lyric"] = (
                f"{current_state['artist']} - {current_state['track']}"
            )

        time.sleep(1)

# ---------------- API ----------------

@app.route("/callback")
def callback():

    code = request.args.get("code")

    if code:

        auth_queue["code"] = code

        return "<h1>Spotify Authorized. You can close this.</h1>"

    return "Authorization failed."

@app.route("/status")
def status():
    return jsonify(current_state)

# ---------------- START ----------------

if __name__ == "__main__":

    threading.Thread(target=background_loop, daemon=True).start()

    app.run(port=8888, debug=False, use_reloader=False)