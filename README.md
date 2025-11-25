# Floating Spotify Lyrics 🎵

A Desktop Overlay Background widget that displays real-time, synchronized lyrics for your currently playing Spotify track. It features a modern, floating UI with a "Glow" effect, drag-and-drop positioning, and seamless backend integration.

## Purpose

To create a distraction free environment while working and when you cannot remember lyrics and want to sing along but are doing some work in your machine. You will get overlayed transparent display with lyrics and just go with the flow of your work along with the lyrics.

## Executable ZIP File

You can even download the Excutable folder from here and run FloatingReminder.exe file and follow the steps of authorization then check the Sync Spotify Radio Button and Click on Float Text Button You Will See the Live Lyrics of Currently Played Songs in Your Spotify App\
https://drive.google.com/drive/folders/1ye43QKGBfxardlmybfGAzs7D3K9jabJ2?usp=sharing

## 🏗 Architecture

The application follows a Master/Slave architecture:

Backend (Python): Runs a local Flask server (localhost:8888). It handles Spotify OAuth2, fetches playback state, searches lrclib.net for lyrics, and performs the time-sync logic.

Frontend (WPF/.NET 4.7.2): A transparent, "Always on Top" window that polls the Python backend every 150ms to update the displayed text.

## 🛠️ Prerequisites for Developers

Before you begin, ensure you have the following installed:

Windows 10 or 11

Visual Studio 2019/2022 (with .NET Desktop Development workload)

Python 3.8+ (Added to System PATH)

Git

## 🚀 Setting Up the Development Environment

1. Clone the Repository

```sh
git clone https://github.com/BitsJayMehta173/FloatSpotify
cd FloatSpotify
```

2. Python Backend Setup

The backend requires specific libraries to handle API requests and fuzzy matching.

Open a terminal in the root directory.

Install dependencies:

```sh
pip install -r requirements.txt
```

3. C# / WPF Setup

Open FloatingReminder.sln in Visual Studio.

Restore NuGet Packages:

Right-click the Solution in Solution Explorer -> Restore NuGet Packages.

Crucial: Ensure System.Text.Json is installed.

🐞 How to Debug (Developer Workflow)

The application handles the Python backend automatically, but for debugging, you need to ensure the files are in the right place.

The "Hybrid" Debug Logic

The MainWindow.xaml.cs contains logic to look for the backend in this order:

now_playing.exe (Compiled Production Build) (In : FloatSpotify/bin/Debug/ (or bin/Debug/net472/). )

now_playing.py (Development Script) (In : FloatSpotify/bin/Debug/ (or bin/Debug/net472/). )

To debug successfully, follow these steps:

Locate your Debug Folder:

Usually: FloatSpotify/bin/Debug/ (or bin/Debug/net472/).

Note: This is where Visual Studio builds your .exe.

Run from Visual Studio:

Press F5 (Start Debugging).

The WPF Dashboard will launch.

A Black Console Window will appear. DO NOT CLOSE THIS. This is the Python Flask server running in debug mode.

Note: If you want to hide this window, change CreateNoWindow = false to true in MainWindow.xaml.cs.

First Run Authorization

When the Python console starts, if it doesn't find a spotify_session.json file, it will open your default browser.

Log in to Spotify and authorize the app.

You will be redirected to localhost:8888/callback.

Close the browser. The Python console should now say [BG] Authorization Successful.

📦 Building for Release

To create a standalone folder that works on machines without Python installed:

Compile Python to EXE:

```sh
pyinstaller --onefile now_playing.py
```

Copy the resulting dist/now_playing.exe to your project folder.

Update C# Logic (Optional):

Ensure StartPythonBackend in MainWindow.xaml.cs is set to prioritize the .exe.

Build WPF App:

Set Visual Studio to Release mode.

Build Solution.

Assemble Distribution:
Create a folder containing:

FloatingReminder.exe (and config/dlls from bin/Release)

now_playing.exe

Fonts/ folder (Important!)

🔑 Spotify API Config

The now_playing.py file contains a Client ID. If you plan to modify this or use your own app credentials:

Go to the Spotify Developer Dashboard.

Create an App.

Set Redirect URI to: http://127.0.0.1:8888/callback

Update CLIENT_ID in now_playing.py.

🤝 Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.
