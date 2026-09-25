import unittest

import recovery

import release


class PublishedPortsTests(unittest.TestCase):
    @staticmethod
    def config():
        def port(host, published, target, protocol):
            return {
                "host_ip": host,
                "published": str(published),
                "target": target,
                "protocol": protocol,
            }

        return {
            "services": {
                "caddy": {"ports": [port("0.0.0.0", 80, 80, "tcp"), port("0.0.0.0", 443, 443, "tcp")]},
                "game": {"ports": [port("0.0.0.0", number, number, "udp") for number in range(7777, 7782)]},
                "master": {},
                "postgres": {},
                "migrate": {},
            }
        }

    def test_public_profile_admits_only_https_and_five_gameplay_ports(self):
        release.check_published_ports(self.config(), disposable=False)

    def test_raw_master_and_extra_gameplay_port_block_release(self):
        config = self.config()
        config["services"]["master"]["ports"] = [{"published": "8080", "target": 8080, "protocol": "tcp"}]
        with self.assertRaises(release.ReleaseError):
            release.check_published_ports(config, disposable=False)

        config = self.config()
        config["services"]["game"]["ports"].append(
            {"host_ip": "0.0.0.0", "published": "7777", "target": 7777, "protocol": "tcp"}
        )
        with self.assertRaises(release.ReleaseError):
            release.check_published_ports(config, disposable=False)

    def test_disposable_profile_rejects_public_binding(self):
        config = self.config()
        for service in ("caddy", "game"):
            for binding in config["services"][service]["ports"]:
                binding["host_ip"] = "127.0.0.1"
                binding["published"] = str(int(binding["published"]) + (18000 if service == "caddy" else 10000))
        release.check_published_ports(config, disposable=True)
        config["services"]["caddy"]["ports"][1]["host_ip"] = "0.0.0.0"
        with self.assertRaises(release.ReleaseError):
            release.check_published_ports(config, disposable=True)


class RestoreSafetyTests(unittest.TestCase):
    def test_restore_rejects_archive_keys_that_escape_its_isolated_target(self):
        from pathlib import Path
        for key in ("postgres/..dump", "postgres/.dump", "postgres/../live.dump",
                    "postgres/../../live.dump", "other/live.dump"):
            with self.subTest(key=key), self.assertRaises(release.ReleaseError):
                recovery.restore(Path("/no-live-state"), Path("/no-credentials"), key)


class LogPrivacyTests(unittest.TestCase):
    def test_proxy_outage_preserves_error_without_token_url_or_authorization(self):
        raw = (b'caddy-1  | {"logger":"http.log.error","msg":"dial tcp: connection refused",'
               b'"request":{"uri":"/lobby?access_token=secret-jwt",'
               b'"headers":{"Authorization":["Bearer secret-jwt"]}},"status":502}\n')
        safe = release.safe_log_line(raw)
        self.assertIn(b"connection refused", safe)
        self.assertIn(b'"status":502', safe)
        self.assertNotIn(b"secret-jwt", safe)
        self.assertNotIn(b'"request"', safe)

    def test_prior_master_request_and_token_lines_are_not_archived(self):
        self.assertEqual(b"", release.safe_log_line(
            b"master-1  | Request starting HTTP/1.1 GET https://example/lobby?access_token=secret\n"))
        self.assertEqual(b"", release.safe_log_line(
            b"master-1  | Authorization: Bearer secret\n"))
        self.assertIn(b"Registered", release.safe_log_line(b"game-1  | [Registration] Registered\n"))


if __name__ == "__main__":
    unittest.main()
