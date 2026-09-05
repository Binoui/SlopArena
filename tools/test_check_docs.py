from __future__ import annotations

import contextlib
import io
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import check_docs


class CheckDocsTests(unittest.TestCase):
    def make_repo(self) -> Path:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        self.git(root, "init")
        for relative in check_docs.CANONICAL_PATHS:
            path = root / relative
            if relative.endswith(".md"):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.touch()
            else:
                path.mkdir(parents=True, exist_ok=True)
        return root

    @staticmethod
    def git(root: Path, *arguments: str) -> None:
        subprocess.run(
            ["git", *arguments],
            cwd=root,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=True,
        )

    def run_checker(self, root: Path) -> tuple[int, str]:
        output = io.StringIO()
        with mock.patch.object(check_docs, "ROOT", root), contextlib.redirect_stdout(output):
            result = check_docs.main()
        return result, output.getvalue()

    def test_scans_nonignored_untracked_docs_not_ignored_vendor(self) -> None:
        root = self.make_repo()
        (root / ".gitignore").write_text(".agents/\n", encoding="utf-8")
        (root / "README.md").write_text(
            "[guide](docs/guide.md)\n", encoding="utf-8"
        )
        (root / "docs/guide.md").touch()
        vendor = root / ".agents/vendor/SKILL.md"
        vendor.parent.mkdir(parents=True)
        vendor.write_text("[missing](missing.md)\n", encoding="utf-8")
        self.git(root, "add", ".gitignore", "README.md")

        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

        new_doc = root / "docs/new guide.md"
        new_doc.write_text("[missing](missing.md)\n", encoding="utf-8")
        result, output = self.run_checker(root)
        self.assertEqual(result, 1)
        self.assertIn("docs/new guide.md", output)

        new_doc.write_text("[guide](guide.md)\n", encoding="utf-8")
        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

    def test_force_tracked_ignored_markdown_is_checked(self) -> None:
        root = self.make_repo()
        (root / ".gitignore").write_text(".agents/\n", encoding="utf-8")
        owned = root / ".agents/owned.md"
        owned.parent.mkdir(parents=True)
        owned.write_text("[missing](missing.md)\n", encoding="utf-8")
        self.git(root, "add", ".gitignore")
        self.git(root, "add", "-f", ".agents/owned.md")

        result, output = self.run_checker(root)
        self.assertEqual(result, 1)
        self.assertIn(".agents/owned.md", output)

    def test_deleted_indexed_markdown_is_ignored(self) -> None:
        root = self.make_repo()
        deleted = root / "docs/deleted.md"
        deleted.write_text("[missing](missing.md)\n", encoding="utf-8")
        self.git(root, "add", "docs/deleted.md")
        deleted.unlink()

        result, output = self.run_checker(root)
        self.assertEqual(result, 0, output)

    def test_non_git_root_reports_enumeration_error(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            result, output = self.run_checker(Path(temporary))

        self.assertEqual(result, 1)
        self.assertIn("unable to enumerate project Markdown:", output)


if __name__ == "__main__":
    unittest.main()
