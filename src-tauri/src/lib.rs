use serde_json::{json, Value};
use std::{path::{Path, PathBuf}, process::Command};
use tauri::Manager;

fn gputaskman_path(app: Option<&tauri::AppHandle>) -> Result<PathBuf, String> {
    let mut candidates = Vec::new();
    if let Ok(path) = std::env::var("GPUTASKMAN_EXE") { candidates.push(PathBuf::from(path)); }
    if let Some(app) = app {
        if let Ok(resources) = app.path().resource_dir() { candidates.push(resources.join("gputaskman").join("gputaskman.exe")); }
    }
    if let Ok(current) = std::env::current_exe() {
        if let Some(dir) = current.parent() { candidates.push(dir.join("gputaskman.exe")); }
    }
    candidates.push(PathBuf::from(r"D:\Tools\gputaskman-0.4.2\gputaskman-0.4.2-windows-x64\gputaskman.exe"));
    candidates.into_iter().find(|p| p.is_file()).ok_or_else(|| "ไม่พบ gputaskman.exe".into())
}

fn gpu_snapshot_from_exe(exe: &Path) -> Result<Value, String> {
    let output = Command::new(exe).args(["--json", "--gpu", "0"]).output().map_err(|e| e.to_string())?;
    if !output.status.success() { return Err(String::from_utf8_lossy(&output.stderr).trim().to_string()); }
    let raw: Value = serde_json::from_slice(&output.stdout).map_err(|e| e.to_string())?;
    Ok(json!({
        "taken": raw["taken"], "gpu": raw["gpu"], "vram_total": raw["vram_total"], "vram_used": raw["vram_used"],
        "device": { "util_gpu": raw["device"]["util_gpu"], "temp": raw["device"]["temp"], "power_w": raw["device"]["power_w"] }
    }))
}

#[tauri::command]
fn get_gpu_snapshot(app: tauri::AppHandle) -> Result<Value, String> {
    gpu_snapshot_from_exe(&gputaskman_path(Some(&app))?)
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .setup(|app| {
            use tauri::menu::{MenuBuilder, MenuItemBuilder};
            use tauri::tray::TrayIconBuilder;
            let show = MenuItemBuilder::with_id("show", "Open GPU Monitor").build(app)?;
            let quit = MenuItemBuilder::with_id("quit", "Exit GPU Monitor").build(app)?;
            let menu = MenuBuilder::new(app).items(&[&show, &quit]).build()?;
            let mut tray = TrayIconBuilder::new().menu(&menu).tooltip("GPU Monitor");
            if let Some(icon) = app.default_window_icon() { tray = tray.icon(icon.clone()); }
            tray.on_menu_event(|app, event| match event.id.as_ref() {
                "show" => if let Some(window) = app.get_webview_window("main") { let _ = window.show(); let _ = window.set_focus(); },
                "quit" => app.exit(0),
                _ => {}
            }).build(app)?;
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![get_gpu_snapshot])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event { api.prevent_close(); let _ = window.hide(); }
        })
        .run(tauri::generate_context!())
        .expect("error while running GPU Monitor");
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn reads_real_gpu_snapshot() {
        let snapshot = gpu_snapshot_from_exe(&gputaskman_path(None).expect("gputaskman executable")).expect("GPU snapshot");
        assert_eq!(snapshot["gpu"], "NVIDIA GeForce RTX 3060");
        assert!(snapshot["vram_total"].as_u64().unwrap_or(0) >= 12 * 1024 * 1024 * 1024);
    }
}
