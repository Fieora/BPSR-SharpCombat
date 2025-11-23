# Installation

## Windows
1. Download and install NPCAP
   - https://npcap.com/#download
     - Why do I need this?
     - Npcap is a packet capture library for Windows. It allows applications to capture and transmit network packets bypassing the protocol stack, and has additional features such as support for loopback traffic and 802.11 Wi-Fi monitoring.
     - Also might theorretically work if you have WinPcap or Windivert installed as SharpPcap supports them all, but untested.
1. Download and run the installer [Here](https://github.com/Fieora/BPSR-SharpCombat/releases/latest)
   - Just need the .exe
   - The app should run on it's own after installing
   - You might need to run as admin


## Linux
Untested, unbuilt yet, but coming soon. You shouldn't need any external installs as the packet capture library I use should work natively.

# Updates
* Updates download automatically from the latest version number on the github releases
* When you close the application, if the update is ready, it will prompt to install the update, which will be available the next time you launch
* In Progress: Indicator that an update is available

# About
Q: Why another DPS meter for Blue Protocol?

A: I used, and tried to work on other projects, but for the most part, people aren't interested in colaborating, quit the game, or wouldn't update for the community.

Q: What makes this project different?

A: I've seen a lot of feedback on other meters, and want to address them. The first thing I wanted to address was customization. You can change lots of things, like bar height, background color/transparancy, fonts and more to come...

Q: What technologies is this built on?

A: Blazor for the frontend, C# for the "server" and it's distributed as an Electron app (like discord). This structure also let's it run anywhere, windows, linux or god forbid Macs
