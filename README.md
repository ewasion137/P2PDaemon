# P2PDaemon
P2P File Daemon is a file transfer service, that doesnt need to send files on some crappy web-services.

- What is this?

This is a program that lets you send files **up to 50GB!** And thats - without any web services.

- How it works?

### 1. Send file (Pusher)
1. Select `push_file` mode.
2. Drag & Drop the file / Type the destination to the file into the console window.
3. Set a **Secret Key**.
4. Share your IP and Key with the receiver.
5. Wait for the secure connection.

### 2. Receive File (Puller)
1. Select `pull_file` mode.
2. Enter the **Sender's IP** (e.g., `26.123.45.67`).
3. Enter the **Secret Key**.
4. If authentication succeeds, the download starts.

***

Pros: 
1. Uses FileStream, so sending/recieving is fast. **And even if you send a giant file - the RAM is not affected much!**
2. Security: Uses SHA-256 with random salt.
3. Direct TCP/IP connection. No cloud, no limits, no middleman.
4. It's interface is terminal, so its easy to use.
### 5. Linux compabillity.

***

# SMALL MINUS!
### If the Reciever (Puller) write incorrect password, *BOTH* of the sessions (Pusher too) will be stopped and pusher will need to peer the file again.

***

**Made on C#, Console.**
*Addictional info: Nugets: Spectre.Console, System.Text.JSON | Made in Visual Studio Community 2026 | Uses: .NET 8.0*

Made with help of Gemini AI: I'm only learning making things like these.
<img width="604" height="244" alt="image" src="https://github.com/user-attachments/assets/8c1558ce-c76f-4d7d-8802-97662b4b158b" />
