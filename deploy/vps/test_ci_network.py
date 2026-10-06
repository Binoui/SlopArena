import os
import shutil
import subprocess
import secrets
import sys
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent
INPUT_GUARD = ROOT / "ci-network-firewall"
DOCKER_GUARD = ROOT / "docker-firewall.sh"
LISTENER = "import socket,sys; s=socket.socket(socket.AF_INET6 if ':' in sys.argv[1] else socket.AF_INET); s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); s.bind((sys.argv[1],int(sys.argv[2]))); s.listen();\nwhile True:\n c,_=s.accept(); c.close()"
CLIENT = "import socket,sys; s=socket.socket(socket.AF_INET6 if ':' in sys.argv[1] else socket.AF_INET); s.settimeout(1); source=sys.argv[3]; s.bind((source,0)) if source else None; s.connect((sys.argv[1],int(sys.argv[2]))); s.close()"
STREAM_SERVER = """import socket, sys, time
s = socket.socket()
s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.bind((sys.argv[1], int(sys.argv[2])))
s.listen()
while True:
    c, _ = s.accept()
    try:
        while True:
            c.sendall(b"x")
            time.sleep(0.02)
    except ConnectionError:
        pass
    finally:
        c.close()
"""
STREAM_CLIENT = """import socket, sys, time
s = socket.create_connection((sys.argv[1], int(sys.argv[2])), timeout=2)
assert s.recv(1) == b"x"
print("established", flush=True)
sys.stdin.readline()
s.setblocking(False)
deadline = time.monotonic() + 0.3
while time.monotonic() < deadline:
    try:
        if not s.recv(4096):
            sys.exit(3)
    except BlockingIOError:
        time.sleep(0.01)
s.settimeout(0.8)
try:
    s.recv(1)
except socket.timeout:
    sys.exit(0)
sys.exit(3)
"""


class PrivateNetworkFirewallTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if os.geteuid() != 0:
            raise unittest.SkipTest("real-kernel namespace tests require root/CAP_NET_ADMIN")
        missing = [tool for tool in ("ip", "iptables", "ip6tables") if shutil.which(tool) is None]
        if missing:
            raise unittest.SkipTest("missing namespace firewall tools: " + ", ".join(missing))
        if not os.access(INPUT_GUARD, os.X_OK):
            raise RuntimeError(f"{INPUT_GUARD} must be executable")
        token = secrets.token_hex(4)
        cls.server, cls.peer, cls.backend = (f"sc{token}{suffix}" for suffix in ("s", "p", "b"))
        cls.namespaces = (cls.server, cls.peer, cls.backend)
        cls.created_namespaces = []
        cls.listeners = []
        cls.addClassCleanup(cls._cleanup)
        try:
            cls._setup_network()
        except subprocess.CalledProcessError as exc:
            cls._cleanup()
            detail = (exc.stderr or b"").decode(errors="replace") if isinstance(exc.stderr, bytes) else str(exc.stderr)
            if "Operation not permitted" in detail or "Permission denied" in detail:
                raise unittest.SkipTest("kernel denied network namespace/firewall capabilities") from exc
            raise

    @classmethod
    def _run(cls, *args, ns=None, check=True):
        command = ["ip", "netns", "exec", ns, *map(str, args)] if ns else list(map(str, args))
        return subprocess.run(command, check=check, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    @classmethod
    def _setup_network(cls):
        for ns in cls.namespaces:
            cls._run("ip", "netns", "add", ns)
            cls.created_namespaces.append(ns)
            cls._run("ip", "link", "set", "lo", "up", ns=ns)
        cls._run("ip", "link", "add", "sloparena-ci", "type", "veth", "peer", "name", "peer0", ns=cls.server)
        cls._run("ip", "link", "set", "peer0", "netns", cls.peer, ns=cls.server)
        cls._run("ip", "addr", "add", "10.253.253.1", "peer", "10.253.253.2/32", "dev", "sloparena-ci", ns=cls.server)
        cls._run("ip", "link", "set", "sloparena-ci", "up", ns=cls.server)
        cls._run("ip", "route", "add", "10.253.253.3/32", "dev", "sloparena-ci", ns=cls.server)
        cls._run("ip", "addr", "add", "10.253.253.2", "peer", "10.253.253.1/32", "dev", "peer0", ns=cls.peer)
        cls._run("ip", "addr", "add", "10.253.253.3/32", "dev", "peer0", ns=cls.peer)
        cls._run("ip", "link", "set", "peer0", "up", ns=cls.peer)
        cls._run("sysctl", "-q", "-w", "net.ipv6.conf.sloparena-ci.disable_ipv6=0", ns=cls.server)
        cls._run("sysctl", "-q", "-w", "net.ipv6.conf.peer0.disable_ipv6=0", ns=cls.peer)
        cls._run("ip", "-6", "addr", "add", "fd42:253:253::1/64", "dev", "sloparena-ci", "nodad", ns=cls.server)
        cls._run("ip", "-6", "addr", "add", "fd42:253:253::2/64", "dev", "peer0", "nodad", ns=cls.peer)

        cls._run("ip", "link", "add", "lan0", "type", "veth", "peer", "name", "back0", ns=cls.server)
        cls._run("ip", "link", "set", "back0", "netns", cls.backend, ns=cls.server)
        cls._run("ip", "addr", "add", "198.18.0.1/24", "dev", "lan0", ns=cls.server)
        cls._run("ip", "link", "set", "lan0", "up", ns=cls.server)
        cls._run("ip", "addr", "add", "198.18.0.2/24", "dev", "back0", ns=cls.backend)
        cls._run("ip", "link", "set", "back0", "up", ns=cls.backend)
        cls._run("ip", "route", "add", "198.18.0.0/24", "via", "10.253.253.1", "dev", "peer0", ns=cls.peer)
        cls._run("ip", "route", "add", "10.253.253.0/24", "via", "198.18.0.1", "dev", "back0", ns=cls.backend)
        cls._run("sysctl", "-q", "-w", "net.ipv4.ip_forward=1", ns=cls.server)

    @classmethod
    def _listen(cls, ns, address, port, program=LISTENER):
        process = subprocess.Popen(
            ["ip", "netns", "exec", ns, sys.executable, "-c", program, address, str(port)],
            stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
        )
        cls.listeners.append(process)
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise AssertionError(f"listener {address}:{port} exited: {process.stderr.read().decode()}")
            try:
                cls._connect(ns, address, port)
                return
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired):
                time.sleep(0.03)
        raise AssertionError(f"listener {address}:{port} did not become reachable")

    @classmethod
    def _connect(cls, ns, address, port, source=""):
        return cls._run(sys.executable, "-c", CLIENT, address, port, source, ns=ns)

    @classmethod
    def _cleanup(cls):
        for process in getattr(cls, "listeners", []):
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=1)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            for stream in (process.stdin, process.stdout, process.stderr):
                if stream is not None:
                    stream.close()
        for ns in getattr(cls, "created_namespaces", []):
            subprocess.run(["ip", "netns", "del", ns], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        cls.created_namespaces = []

    def test_real_kernel_allows_only_private_ssh_and_denies_forwarding(self):
        # Prove each endpoint, route, and alternate source is live before policy can reject it.
        self._listen(self.server, "10.253.253.1", 2223)
        self._listen(self.server, "10.253.253.1", 2224)
        self._listen(self.server, "fd42:253:253::1", 2223)
        self._listen(self.backend, "198.18.0.2", 80)
        self._listen(self.peer, "10.253.253.2", 80)
        self._listen(self.backend, "198.18.0.2", 81, program=STREAM_SERVER)
        self._connect(self.peer, "10.253.253.1", 2223)
        self._connect(self.peer, "10.253.253.1", 2224)
        self._connect(self.peer, "10.253.253.1", 2223, "10.253.253.3")
        self._connect(self.peer, "fd42:253:253::1", 2223)
        self._connect(self.peer, "198.18.0.2", 80)
        self._connect(self.backend, "10.253.253.2", 80)
        stream = subprocess.Popen(
            ["ip", "netns", "exec", self.peer, sys.executable, "-c", STREAM_CLIENT, "198.18.0.2", "81"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        )
        self.listeners.append(stream)
        self.assertEqual(stream.stdout.readline(), b"established\n")

        try:
            self._run(str(INPUT_GUARD), "up", ns=self.server)
            self._run(str(DOCKER_GUARD), ns=self.server)
            self._run(str(INPUT_GUARD), "--verify", ns=self.server)
        except subprocess.CalledProcessError as exc:
            detail = (exc.stderr or b"").decode(errors="replace")
            if "Operation not permitted" in detail or "Permission denied" in detail:
                self.skipTest("kernel denied isolated firewall capabilities")
            raise
        self.assertEqual(self._connect(self.peer, "10.253.253.1", 2223).returncode, 0)
        stream.stdin.write(b"probe\n")
        stream.stdin.flush()
        self.assertEqual(stream.wait(timeout=4), 0, stream.stderr.read().decode())
        with self.assertRaises(subprocess.CalledProcessError):
            self._connect(self.backend, "10.253.253.2", 80)
        for address, port, source in (
            ("10.253.253.1", 2224, ""),
            ("10.253.253.1", 2223, "10.253.253.3"),
            ("fd42:253:253::1", 2223, ""),
            ("198.18.0.2", 80, ""),
        ):
            with self.subTest(address=address, port=port, source=source):
                with self.assertRaises(subprocess.CalledProcessError):
                    self._connect(self.peer, address, port, source)


if __name__ == "__main__":
    unittest.main()
