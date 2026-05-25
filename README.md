# MoveBox (Project Antigravity)

MoveBox is a prototype PvP Arena Brawler featuring a **server-authoritative** architecture with **client-side prediction (CSP)** and **server reconciliation**, developed in C# using Godot 4.

---

## 🛠️ Required Dependencies

To run this project, you must install the .NET SDK on your system.

### On Arch Linux / CachyOS:
```bash
sudo pacman -S dotnet-sdk-8.0
```

### Godot Version:
You must use the **Godot Engine - .NET (Mono)** version to compile and run C# code.
- If you import the project into Godot via Steam or a standard launcher, make sure you downloaded the .NET version from the official Godot website.

---

## 📁 Project Structure

The project is structured as a unified .NET solution containing 3 projects:
1. **`MoveBox`** (Root Project): The Godot 4 C# rendering client (contains `Main.cs`, `main.tscn`, `project.godot`).
2. **`Shared/`**: Shared C# library containing the network packet structures (`ClientInputPacket`, `CharacterStatePacket`) and physics constants (`PhysicsConfig.cs`).
3. **`Server/`**: .NET console application running the authoritative physics simulation at 60Hz.

---

## 🚀 Running the Project

### 1. Start the Authoritative Physics Server
In your terminal at the root of the project, run the helper script:
```bash
./run_server.sh
```
Or run it manually using the `dotnet` CLI:
```bash
dotnet run --project Server/MoveBox.Server.csproj
```
The server will start and listen on UDP port `7777`.

### 2. Start the Client (Godot)
1. Open your **Godot Engine (.NET)** editor.
2. Import the project by selecting the `project.godot` file located at the root of `/home/binoui/Documents/projects/MoveBox/`.
3. Click **Play** (or press F5) to run the main scene.

---

## 🎮 Controls (Client Sandbox)

- **ZQSD / WASD**: Move around the arena.
- **Shift**: Dash in the current movement direction (has a cooldown).
- **Visuals**:
  - **Cyan Circle**: Your local character (predicted instantly at 0 ms latency).
  - **Red Circle**: The authoritative server position (ghost representation).
  - The client automatically performs a seamless reconciliation replay loop if a drift is detected.
