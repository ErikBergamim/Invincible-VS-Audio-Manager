"""
Magpie Podcast Downloader
=========================
Opens Chrome via undetected-chromedriver, navigates to the Magpie Podcast
members page, clicks every "Ouvir Episódio" button, captures the MP3 URLs
from the <audio> element, and downloads them all.

Provides a tkinter GUI with:
  - URL field (pre-filled with the episodes page)
  - Output folder picker
  - Start / Stop controls
  - Real-time log and progress bar
"""

import tkinter as tk
from tkinter import ttk, scrolledtext, filedialog, messagebox
import threading
import time
from datetime import datetime
import undetected_chromedriver as uc
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.common.exceptions import TimeoutException, NoSuchElementException
import re
import os
import subprocess
import json
import urllib.request
from selenium import webdriver
from http.server import HTTPServer, BaseHTTPRequestHandler


class MagpieDownloaderApp:
    """Main application class with tkinter GUI."""

    DEFAULT_URL = "https://membros.magpiepodcast.com.br/episodes"

    def __init__(self, root: tk.Tk):
        self.root = root
        self.root.title("Magpie Podcast Downloader")
        self.root.geometry("820x650")
        self.root.minsize(700, 500)

        self.driver = None
        self.running = False
        self.stop_event = threading.Event()
        self.download_thread = None

        self._build_ui()

    # ------------------------------------------------------------------ UI
    def _build_ui(self):
        pad = {"padx": 8, "pady": 4}

        # --- URL row ---
        frm_url = ttk.LabelFrame(self.root, text="URL da página de episódios")
        frm_url.pack(fill="x", **pad)

        self.url_var = tk.StringVar(value=self.DEFAULT_URL)
        ttk.Entry(frm_url, textvariable=self.url_var, width=80).pack(
            side="left", fill="x", expand=True, padx=4, pady=4
        )

        # --- Output folder row ---
        frm_out = ttk.LabelFrame(self.root, text="Pasta de destino dos MP3s")
        frm_out.pack(fill="x", **pad)

        self.out_var = tk.StringVar(value=os.path.join(os.path.expanduser("~"), "MagpiePodcast"))
        ttk.Entry(frm_out, textvariable=self.out_var, width=70).pack(
            side="left", fill="x", expand=True, padx=4, pady=4
        )
        ttk.Button(frm_out, text="Escolher…", command=self._pick_folder).pack(
            side="right", padx=4, pady=4
        )

        # --- Options row ---
        frm_opts = ttk.LabelFrame(self.root, text="Opções")
        frm_opts.pack(fill="x", **pad)

        self.headless_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(frm_opts, text="Headless (sem janela do Chrome)", variable=self.headless_var).pack(
            side="left", padx=4, pady=4
        )

        self.wait_login_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(frm_opts, text="Aguardar login manual (30 s)", variable=self.wait_login_var).pack(
            side="left", padx=4, pady=4
        )

        self.scroll_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(frm_opts, text="Scroll automático (carregar todos)", variable=self.scroll_var).pack(
            side="left", padx=4, pady=4
        )

        # --- Buttons row ---
        frm_btn = ttk.Frame(self.root)
        frm_btn.pack(fill="x", **pad)

        self.btn_start = ttk.Button(frm_btn, text="▶  Iniciar", command=self._on_start)
        self.btn_start.pack(side="left", padx=4)

        self.btn_stop = ttk.Button(frm_btn, text="■  Parar", command=self._on_stop, state="disabled")
        self.btn_stop.pack(side="left", padx=4)

        # --- Progress ---
        self.progress_var = tk.DoubleVar(value=0)
        self.progress = ttk.Progressbar(self.root, variable=self.progress_var, maximum=100)
        self.progress.pack(fill="x", **pad)

        self.status_var = tk.StringVar(value="Pronto.")
        ttk.Label(self.root, textvariable=self.status_var, anchor="w").pack(fill="x", **pad)

        # --- Log ---
        frm_log = ttk.LabelFrame(self.root, text="Log")
        frm_log.pack(fill="both", expand=True, **pad)

        self.log = scrolledtext.ScrolledText(frm_log, height=15, state="disabled", wrap="word")
        self.log.pack(fill="both", expand=True, padx=4, pady=4)

    # ------------------------------------------------------------------ helpers
    def _pick_folder(self):
        folder = filedialog.askdirectory(title="Selecione a pasta de destino")
        if folder:
            self.out_var.set(folder)

    def _log(self, msg: str):
        ts = datetime.now().strftime("%H:%M:%S")
        line = f"[{ts}] {msg}\n"

        def _append():
            self.log.configure(state="normal")
            self.log.insert("end", line)
            self.log.see("end")
            self.log.configure(state="disabled")

        self.root.after(0, _append)

    def _set_status(self, msg: str):
        self.root.after(0, lambda: self.status_var.set(msg))

    def _set_progress(self, value: float):
        self.root.after(0, lambda: self.progress_var.set(value))

    # ------------------------------------------------------------------ start / stop
    def _on_start(self):
        if self.running:
            return
        self.stop_event.clear()
        self.running = True
        self.btn_start.configure(state="disabled")
        self.btn_stop.configure(state="normal")
        self.download_thread = threading.Thread(target=self._worker, daemon=True)
        self.download_thread.start()

    def _on_stop(self):
        self._log("Parando…")
        self.stop_event.set()

    def _finish(self, msg: str = "Concluído."):
        self.running = False
        self._set_status(msg)
        self.root.after(0, lambda: self.btn_start.configure(state="normal"))
        self.root.after(0, lambda: self.btn_stop.configure(state="disabled"))

    # ------------------------------------------------------------------ core
    def _worker(self):
        error_msg = None
        try:
            self._run_pipeline()
        except Exception as exc:
            self._log(f"ERRO: {exc}")
            error_msg = f"Erro: {exc}"
        finally:
            if self.driver:
                try:
                    self.driver.quit()
                except Exception:
                    pass
                self.driver = None
            self._finish(error_msg or "Concluído.")

    def _run_pipeline(self):
        out_dir = self.out_var.get().strip()
        os.makedirs(out_dir, exist_ok=True)

        url = self.url_var.get().strip()
        if not url:
            self._log("URL vazia, abortando.")
            return

        # --- launch Chrome ---
        self._set_status("Abrindo Chrome…")
        self._log("Iniciando undetected-chromedriver…")
        options = uc.ChromeOptions()
        if self.headless_var.get():
            options.add_argument("--headless=new")
        options.add_argument("--disable-blink-features=AutomationControlled")
        options.add_argument("--no-sandbox")
        options.add_argument("--disable-gpu")
        options.add_argument("--window-size=1280,900")

        self.driver = uc.Chrome(options=options)
        self.driver.set_page_load_timeout(60)

        # --- navigate ---
        self._log(f"Navegando para {url}")
        self.driver.get(url)

        # --- wait for login if needed ---
        if self.wait_login_var.get():
            self._set_status("Aguardando login manual (faça login no Chrome)…")
            self._log("Aguardando login… Faça login no Chrome aberto. "
                      "O script continua automaticamente ao detectar os botões.")
            try:
                WebDriverWait(self.driver, 120).until(
                    EC.presence_of_element_located((By.XPATH, "//button[contains(., 'Ouvir')]"))
                )
                self._log("Página de episódios detectada!")
            except TimeoutException:
                self._log("Timeout aguardando login/episódios. Tentando continuar…")

        if self.stop_event.is_set():
            return

        # --- scroll to load all episodes ---
        if self.scroll_var.get():
            self._scroll_to_bottom()

        if self.stop_event.is_set():
            return

        # --- collect episode buttons ---
        self._set_status("Coletando botões de episódios…")
        buttons = self.driver.find_elements(
            By.XPATH, "//button[contains(., 'Ouvir Episódio')]"
        )
        total = len(buttons)
        self._log(f"Encontrados {total} botões 'Ouvir Episódio'.")

        if total == 0:
            self._log("Nenhum botão encontrado. Verifique se o login foi feito.")
            return

        # --- click each button and capture audio src ---
        collected: list[dict] = []
        seen_urls: set[str] = set()

        for idx in range(total):
            if self.stop_event.is_set():
                self._log("Interrompido pelo usuário.")
                break

            pct = (idx / total) * 50
            self._set_progress(pct)
            self._set_status(f"Coletando URLs… ({idx + 1}/{total})")

            # Re-fetch buttons (DOM may change after clicks/scrolls)
            buttons = self.driver.find_elements(
                By.XPATH, "//button[contains(., 'Ouvir Episódio')]"
            )
            if idx >= len(buttons):
                self._log(f"Botão {idx + 1} não encontrado (DOM mudou). Pulando.")
                continue

            btn = buttons[idx]

            # Scroll button into view
            self.driver.execute_script(
                "arguments[0].scrollIntoView({block: 'center'});", btn
            )
            time.sleep(0.3)

            # Try to get the episode title from the card
            title = self._get_episode_title(btn, idx)

            # Click the button
            try:
                btn.click()
            except Exception as e:
                self._log(f"  Erro ao clicar botão {idx + 1}: {e}")
                try:
                    self.driver.execute_script("arguments[0].click();", btn)
                except Exception:
                    self._log(f"  Fallback click também falhou, pulando.")
                    continue

            # Wait for the <audio> src to update
            audio_url = self._wait_for_audio_src(seen_urls, timeout=8)
            if audio_url:
                seen_urls.add(audio_url)
                collected.append({"title": title, "url": audio_url})
                self._log(f"  [{idx + 1}/{total}] {title} → {audio_url.split('/')[-1]}")
            else:
                self._log(f"  [{idx + 1}/{total}] {title} → SEM URL (já vista ou timeout)")

            time.sleep(0.3)

        self._log(f"\nTotal de URLs únicas coletadas: {len(collected)}")

        # Save URL list as JSON
        json_path = os.path.join(out_dir, "episodes.json")
        with open(json_path, "w", encoding="utf-8") as f:
            json.dump(collected, f, ensure_ascii=False, indent=2)
        self._log(f"Lista salva em: {json_path}")

        if self.stop_event.is_set():
            return

        # --- download MP3s ---
        self._download_all(collected, out_dir)

    def _scroll_to_bottom(self):
        """Scroll down repeatedly to trigger lazy-loading of all episodes."""
        self._set_status("Scrollando para carregar todos os episódios…")
        self._log("Scrollando para carregar todos os episódios…")

        last_height = 0
        stable_count = 0
        max_scrolls = 200

        for i in range(max_scrolls):
            if self.stop_event.is_set():
                return

            self.driver.execute_script("window.scrollTo(0, document.body.scrollHeight);")
            time.sleep(1.5)

            new_height = self.driver.execute_script("return document.body.scrollHeight")
            btn_count = len(self.driver.find_elements(
                By.XPATH, "//button[contains(., 'Ouvir Episódio')]"
            ))

            if new_height == last_height:
                stable_count += 1
            else:
                stable_count = 0

            last_height = new_height

            if stable_count >= 3:
                self._log(f"Scroll completo. {btn_count} episódios carregados.")
                break

            if i % 5 == 0:
                self._log(f"  Scroll #{i + 1}: {btn_count} episódios até agora…")

        # Scroll back to top
        self.driver.execute_script("window.scrollTo(0, 0);")
        time.sleep(0.5)

    def _get_episode_title(self, btn, idx: int) -> str:
        """Try to extract episode title from the card containing the button."""
        try:
            # Navigate up to the card container and look for title text
            card = btn.find_element(By.XPATH, "./ancestor::div[contains(@class, 'rounded')]")
            # Look for prominent text elements
            for selector in [
                ".//h2", ".//h3",
                ".//span[contains(@class, 'font-bold')]",
                ".//p[contains(@class, 'font-bold')]",
                ".//div[contains(@class, 'font-bold')]",
                ".//span[contains(@class, 'font-semibold')]",
            ]:
                try:
                    el = card.find_element(By.XPATH, selector)
                    text = el.text.strip()
                    if text and len(text) > 3:
                        return text
                except NoSuchElementException:
                    continue
        except Exception:
            pass
        return f"Episodio_{idx + 1:03d}"

    def _wait_for_audio_src(self, seen: set[str], timeout: int = 8) -> str | None:
        """Wait until the <audio> element's src changes to a new URL."""
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                audio_el = self.driver.find_element(By.TAG_NAME, "audio")
                src = audio_el.get_attribute("src") or ""
                if src and src.startswith("http") and src not in seen:
                    return src
            except NoSuchElementException:
                pass
            time.sleep(0.3)
        return None

    def _download_all(self, episodes: list[dict], out_dir: str):
        """Download every collected MP3."""
        total = len(episodes)
        if total == 0:
            self._log("Nenhum arquivo para baixar.")
            return

        self._set_status("Baixando MP3s…")
        self._log(f"\nIniciando download de {total} arquivo(s)…")

        for idx, ep in enumerate(episodes):
            if self.stop_event.is_set():
                self._log("Download interrompido pelo usuário.")
                break

            pct = 50 + (idx / total) * 50
            self._set_progress(pct)

            title = ep["title"]
            url = ep["url"]

            # Sanitize filename
            safe_name = re.sub(r'[\\/*?:"<>|]', '_', title).strip()
            if not safe_name:
                safe_name = f"episodio_{idx + 1:03d}"
            filename = f"{idx + 1:03d}_{safe_name}.mp3"
            filepath = os.path.join(out_dir, filename)

            # Skip if already downloaded
            if os.path.exists(filepath):
                self._log(f"  [{idx + 1}/{total}] Já existe: {filename}")
                continue

            self._set_status(f"Baixando ({idx + 1}/{total}): {filename}")
            self._log(f"  [{idx + 1}/{total}] Baixando: {filename}")

            try:
                self._download_file(url, filepath)
                size_mb = os.path.getsize(filepath) / (1024 * 1024)
                self._log(f"    ✓ {size_mb:.1f} MB")
            except Exception as e:
                self._log(f"    ERRO: {e}")

        self._set_progress(100)
        self._log(f"\nDownload concluído! Arquivos em: {out_dir}")
        self._set_status("Concluído!")

    def _download_file(self, url: str, filepath: str):
        """Download a file with progress tracking using urllib."""
        tmp_path = filepath + ".part"
        try:
            req = urllib.request.Request(url)
            req.add_header("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)")
            with urllib.request.urlopen(req, timeout=300) as response:
                total_size = int(response.headers.get("Content-Length", 0))
                downloaded = 0
                block_size = 1024 * 256  # 256 KB

                with open(tmp_path, "wb") as f:
                    while True:
                        if self.stop_event.is_set():
                            raise InterruptedError("Download cancelado pelo usuário")
                        chunk = response.read(block_size)
                        if not chunk:
                            break
                        f.write(chunk)
                        downloaded += len(chunk)

            os.replace(tmp_path, filepath)
        except Exception:
            if os.path.exists(tmp_path):
                os.remove(tmp_path)
            raise


def main():
    root = tk.Tk()
    MagpieDownloaderApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
