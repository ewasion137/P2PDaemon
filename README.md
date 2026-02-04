# P2P File Daemon

![Build Status](https://img.shields.io/badge/build-passing-brightgreen)
![Platform](https://img.shields.io/badge/platform-win%20%7C%20linux-blue)
![License](https://img.shields.io/badge/license-MIT-orange)
![Security](https://img.shields.io/badge/security-AES--256--CBC-red)

<img src="https://github.com/user-attachments/assets/8c1558ce-c76f-4d7d-8802-97662b4b158b" width="100%" />

---

### What is this?
P2P File Daemon is a file transfer service that doesnt need to send files on some crappy web-services.
This is a program that lets you send files **up to 50GB (and more)!** And thats - without any clouds.

### How it works?

#### 1. Send file (Pusher)
1. Select `push_file` mode.
2. Drag & Drop the file / Type the path into the console window.
3. Set a **Secret Key**.
4. Share your IP and Key with the receiver.
5. Wait for the secure connection.

#### 2. Receive File (Puller)
1. Select `pull_file` mode.
2. Enter the **Sender's IP** (e.g., `26.123.45.67`).
3. Enter the **Secret Key**.
4. If authentication succeeds, the download starts immediately.

---

### Pros:
1. **Speed & Efficiency:** Uses `FileStream`, so sending/receiving is fast. **And even if you send a giant file - the RAM is not affected much!**
2. **Security:** Uses **AES-256** encryption with random salt + SHA-256 integrity check.
3. **No Limits:** Direct TCP/IP connection. No cloud, no limits, no middleman.
4. **UX:** It's interface is terminal based (TUI), so its easy to use.
5. **Cross-platform:** Windows & Linux compatibility.

---

### ⚠️ SMALL MINUS!
If the **Receiver** (Puller) writes an incorrect password, **BOTH** of the sessions (Pusher too) will be stopped for security reasons. The pusher will need to start the transfer again.

---

**Made on C#, Console.**

*Additional info:*
*   **Nugets:** Spectre.Console, System.Text.JSON
*   **IDE:** Visual Studio Community 2026
*   **Framework:** .NET 8.0
*   *Made with help of Gemini AI: I'm only learning making things like these.*

![Windows](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/platform-Linux-FCC624?logo=linux&logoColor=black)
