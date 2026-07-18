#define IMGUI_DEFINE_MATH_OPERATORS
#include "imgui.h"
#include "imgui_internal.h"
#include "imgui_impl_dx9.h"
#include "imgui_impl_win32.h"

#include <Windows.h>
#include <windowsx.h>
#include <d3d9.h>
#include <algorithm>
#include <array>
#include <cctype>
#include <cstring>
#include <string>
#include <vector>

#include "bytes.hpp"
#include "blur.hpp"
#include "gui.hpp"
#include "hashes.hpp"
#include "native_api.h"

using namespace ImGui;

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND, UINT, WPARAM, LPARAM);

namespace {
constexpr int menu_width = 690;
constexpr int menu_height = 500;

LPDIRECT3D9 d3d = nullptr;
LPDIRECT3DDEVICE9 device = nullptr;
D3DPRESENT_PARAMETERS present{};
HWND window_handle = nullptr;
const VectraMenuHostApi* host = nullptr;
VectraMenuState state{};
int active_tab = 0;
std::array<int, 4> active_subtab{};
std::array<std::vector<const char*>, 4> subtabs = {{
    { "General", "Targeting", "Movement" },
    { "Players", "World", "Overlay" },
    { "Session", "Profiles" },
    { "Interface", "Diagnostics", "About" }
}};
char search_text[64]{};
bool waiting_for_key = false;
std::string command_message;

bool create_device(HWND hwnd);
void cleanup_device();
void reset_device();
LRESULT WINAPI wnd_proc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam);

bool matches(const char* label) {
    if (search_text[0] == '\0') return true;
    std::string value(label);
    std::string query(search_text);
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    std::transform(query.begin(), query.end(), query.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return value.find(query) != std::string::npos;
}

void set_bool(VectraBoolSetting setting, bool value) {
    if (host && host->set_bool) host->set_bool(static_cast<int>(setting), value ? 1 : 0);
}

void set_int(VectraIntSetting setting, int value) {
    if (host && host->set_int) host->set_int(static_cast<int>(setting), value);
}

void set_double(VectraDoubleSetting setting, double value) {
    if (host && host->set_double) host->set_double(static_cast<int>(setting), value);
}

void set_string(VectraStringSetting setting, const char* value) {
    if (host && host->set_string) host->set_string(static_cast<int>(setting), value);
}

void command(VectraMenuCommand value) {
    if (!host || !host->execute_command) return;
    VectraCommandResult result{};
    host->execute_command(static_cast<int>(value), &result);
    command_message = result.message;
}

void checkbox(const char* label, int value, VectraBoolSetting setting) {
    if (!matches(label)) return;
    bool checked = value != 0;
    if (ImGui::Checkbox(label, &checked)) set_bool(setting, checked);
}

void slider(const char* label, int value, int minimum, int maximum, VectraIntSetting setting, const char* format = "%d") {
    if (!matches(label)) return;
    int current = value;
    if (ImGui::SliderInt(label, &current, minimum, maximum, format)) set_int(setting, current);
}

void combo(const char* label, int value, const char* const* values, int count, VectraIntSetting setting) {
    if (!matches(label)) return;
    int current = std::clamp(value, 0, count - 1);
    if (ImGui::Combo(label, &current, values, count)) set_int(setting, current);
}

void note(const char* text, const ImVec4& color = ImVec4(.51f, .52f, .56f, 1.f)) {
    PushStyleColor(ImGuiCol_Text, color);
    TextWrapped("%s", text);
    PopStyleColor();
}

void begin_box(const char* title, float width, float height) { gui.group_box(title, ImVec2(width, height)); }
void end_box() { gui.end_group_box(); }

void render_aim() {
    const float half = GetWindowWidth() / 2.f - GetStyle().ItemSpacing.x / 2.f;
    switch (active_subtab[0]) {
    case 0: {
        begin_box(ICON_FA_CROSSHAIRS " Aim assist", half, GetWindowHeight());
        checkbox("Enable aim assist", state.aim_assist_enabled, VectraBoolSetting::AimAssistEnabled);
        const char* activations[] = { "Hold key", "Always" };
        combo("Activation", state.aim_activation, activations, 2, VectraIntSetting::AimActivation);
        if (state.aim_activation == 0 && matches("Activation key")) {
            if (Button(waiting_for_key ? "PRESS A KEY" : "Capture activation key", ImVec2(GetWindowWidth(), 25))) waiting_for_key = true;
            SameLine();
            TextDisabled("VK %02X", state.aim_activation_key);
        }
        checkbox("Enable triggerbot", state.trigger_enabled, VectraBoolSetting::TriggerEnabled);
        end_box();
        SameLine();
        begin_box(ICON_FA_SHIELD " Safety", half, GetWindowHeight());
        note("Input remains locked until Master enable and Private-match authorization are both active.");
        Separator();
        note(state.status_title, ImVec4(.3f, .49f, 1.f, 1.f));
        note(state.status_detail);
        end_box();
        break;
    }
    case 1: {
        begin_box(ICON_FA_BULLSEYE " Target", half, GetWindowHeight());
        const char* points[] = { "Chest", "Head" };
        const char* priorities[] = { "Crosshair", "Closest", "Most visible" };
        combo("Target point", state.aim_target_point, points, 2, VectraIntSetting::AimTargetPoint);
        combo("Target priority", state.aim_priority, priorities, 3, VectraIntSetting::AimPriority);
        checkbox("Require visible target", state.aim_visibility_check_enabled, VectraBoolSetting::AimVisibilityCheckEnabled);
        end_box();
        SameLine();
        begin_box(ICON_FA_EXPAND " Field of view", half, GetWindowHeight());
        checkbox("Draw aim FOV", state.draw_aim_fov, VectraBoolSetting::DrawAimFov);
        slider("FOV pixels", state.aim_assist_fov_pixels, 30, 300, VectraIntSetting::AimAssistFovPixels, "%d px");
        end_box();
        break;
    }
    default: {
        begin_box(ICON_FA_MOUSE " Movement", half, GetWindowHeight());
        const char* movements[] = { "Smooth", "Snap" };
        combo("Aim movement", state.aim_movement, movements, 2, VectraIntSetting::AimMovement);
        if (state.aim_movement == 0) slider("Aim strength", state.aim_assist_strength_percent, 5, 100, VectraIntSetting::AimAssistStrengthPercent, "%d%%");
        end_box();
        SameLine();
        begin_box(ICON_FA_LOCK " Target lock", half, GetWindowHeight());
        note("Smooth follows the current eligible target. Snap moves once for each newly acquired lock.");
        Separator();
        note("Aim assist never sends firing input. Triggerbot remains a separate gated module.");
        end_box();
        break;
    }
    }
}

void render_visuals() {
    const float half = GetWindowWidth() / 2.f - GetStyle().ItemSpacing.x / 2.f;
    switch (active_subtab[1]) {
    case 0: {
        begin_box(ICON_FA_USER " Player ESP", half, GetWindowHeight());
        checkbox("Enable ESP", state.esp_enabled, VectraBoolSetting::EspEnabled);
        checkbox("Corner boxes", state.corner_boxes, VectraBoolSetting::CornerBoxes);
        checkbox("Names", state.draw_names, VectraBoolSetting::DrawNames);
        checkbox("Health bars", state.draw_health, VectraBoolSetting::DrawHealth);
        checkbox("Distance", state.draw_distance, VectraBoolSetting::DrawDistance);
        checkbox("Weapon and ammo", state.draw_weapons, VectraBoolSetting::DrawWeapons);
        end_box();
        SameLine();
        begin_box(ICON_FA_EYE " Player details", half, GetWindowHeight());
        checkbox("Head marker", state.draw_head_marker, VectraBoolSetting::DrawHeadMarker);
        checkbox("Snaplines", state.draw_snaplines, VectraBoolSetting::DrawSnaplines);
        checkbox("Off-screen arrows", state.draw_offscreen_arrows, VectraBoolSetting::DrawOffscreenArrows);
        checkbox("Rotating radar", state.draw_radar, VectraBoolSetting::DrawRadar);
        checkbox("Team check", state.team_check_enabled, VectraBoolSetting::TeamCheckEnabled);
        const char* themes[] = { "Lavender", "Glacier", "Rose" };
        combo("ESP accent", state.esp_theme, themes, 3, VectraIntSetting::EspTheme);
        end_box();
        break;
    }
    case 1:
        begin_box(ICON_FA_GLOBE " World ESP", half, GetWindowHeight());
        checkbox("Item ESP", state.draw_item_esp, VectraBoolSetting::DrawItemEsp);
        checkbox("Bomb ESP", state.draw_bomb_esp, VectraBoolSetting::DrawBombEsp);
        end_box();
        SameLine();
        begin_box(ICON_FA_ROUTE " Grenade predictor", half, GetWindowHeight());
        checkbox("Grenade Predictor", state.draw_grenade_prediction, VectraBoolSetting::DrawGrenadePrediction);
        if (matches("Map selection") && state.map_count > 0) {
            std::vector<const char*> maps;
            int selected = 0;
            for (int i = 0; i < (std::min)(state.map_count, VECTRA_MENU_MAX_MAPS); ++i) {
                maps.push_back(state.maps[i]);
                if (_stricmp(state.maps[i], state.current_map) == 0) selected = i;
            }
            if (Combo("Map selection", &selected, maps.data(), static_cast<int>(maps.size()))) set_string(VectraStringSetting::GrenadePredictionMap, maps[selected]);
        }
        note(state.grenade_status);
        end_box();
        break;
    default:
        begin_box(ICON_FA_DESKTOP " Streaming", half, GetWindowHeight());
        checkbox("Streamproof", state.hide_overlay_from_capture, VectraBoolSetting::HideOverlayFromCapture);
        note("OFF allows supported monitor captures to include the overlay. ON requests Windows capture exclusion.");
        end_box();
        SameLine();
        begin_box(ICON_FA_INFO_CIRCLE " Overlay status", half, GetWindowHeight());
        note(state.status_title, ImVec4(.3f, .49f, 1.f, 1.f));
        note(state.status_detail);
        end_box();
        break;
    }
}

void render_config() {
    const float half = GetWindowWidth() / 2.f - GetStyle().ItemSpacing.x / 2.f;
    if (active_subtab[2] == 0) {
        begin_box(ICON_FA_SHIELD " Private match", half, GetWindowHeight());
        checkbox("Master enable", state.master_enabled, VectraBoolSetting::MasterEnabled);
        checkbox("Private-match authorization", state.private_match_authorized, VectraBoolSetting::PrivateMatchAuthorized);
        note("Authorization is session-only and is reset after loading a configuration.");
        end_box();
        SameLine();
        begin_box(ICON_FA_INFO_CIRCLE " Session status", half, GetWindowHeight());
        note(state.status_title, ImVec4(.3f, .49f, 1.f, 1.f));
        note(state.status_detail);
        end_box();
    } else {
        begin_box(ICON_FA_SAVE " Configuration", half, GetWindowHeight());
        if (Button("Save configuration", ImVec2(GetWindowWidth(), 25))) command(VectraMenuCommand::SaveConfiguration);
        if (Button("Load configuration", ImVec2(GetWindowWidth(), 25))) command(VectraMenuCommand::LoadConfiguration);
        note(command_message.empty() ? state.config_message : command_message.c_str());
        end_box();
        SameLine();
        begin_box(ICON_FA_FOLDER " Storage", half, GetWindowHeight());
        note("Stored locally in %LocalAppData%\\Vectra External\\client-config.json.");
        note("Private-match authorization is never saved or restored.");
        end_box();
    }
}

void render_system() {
    const float half = GetWindowWidth() / 2.f - GetStyle().ItemSpacing.x / 2.f;
    switch (active_subtab[3]) {
    case 0: {
        begin_box(ICON_FA_PALETTE " Interface", half, GetWindowHeight());
        if (matches("Menu opacity")) {
            int opacity = static_cast<int>(std::clamp(state.ui_opacity, .82, 1.0) * 100.0);
            if (SliderInt("Menu opacity", &opacity, 82, 100, "%d%%")) set_double(VectraDoubleSetting::UiOpacity, opacity / 100.0);
        }
        note("The original vectraNewUi palette, layout and animations are used directly.");
        end_box();
        SameLine();
        begin_box(ICON_FA_KEYBOARD " Controls", half, GetWindowHeight());
        note("Use the four sidebar areas and their subtabs. Search filters controls on the active page.");
        note("The top Save button writes the current profile immediately.");
        end_box();
        break;
    }
    case 1:
        begin_box(ICON_FA_HEARTBEAT " Live diagnostics", GetWindowWidth(), GetWindowHeight());
        note(state.diagnostics);
        end_box();
        break;
    default:
        begin_box(ICON_FA_INFO_CIRCLE " Vectra External", half, GetWindowHeight());
        Text("Version %s", state.version);
        Text("CS2 build %s", state.build);
        note("External client for locally hosted or otherwise trusted private matches only.");
        end_box();
        SameLine();
        begin_box(ICON_FA_CODE " Runtime", half, GetWindowHeight());
        note("Native Dear ImGui / DirectX 9 menu with the existing managed runtime and WPF overlay renderer.");
        end_box();
        break;
    }
}

void capture_key() {
    if (!waiting_for_key) return;
    for (int key = 1; key <= 255; ++key) {
        if (!(GetAsyncKeyState(key) & 1)) continue;
        if (key == VK_ESCAPE) { waiting_for_key = false; return; }
        set_int(VectraIntSetting::AimActivationKey, key);
        waiting_for_key = false;
        return;
    }
}

void render_menu() {
    if (host && host->get_state) host->get_state(&state);
    capture_key();
    const BYTE opacity = static_cast<BYTE>(std::clamp(state.ui_opacity, .82, 1.0) * 255.0);
    SetLayeredWindowAttributes(window_handle, 0, opacity, LWA_ALPHA);

    SetNextWindowPos(ImVec2(0, 0), ImGuiCond_Always);
    SetNextWindowSize(ImVec2(static_cast<float>(menu_width), static_cast<float>(menu_height)), ImGuiCond_Always);
    PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(0, 0));
    Begin("Vectra External", nullptr, ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoMove);
    auto* current = GetCurrentWindow();
    auto* draw = current->DrawList;
    const auto pos = current->Pos;
    const auto size = current->Size;
    gui.m_anim = ImLerp(gui.m_anim, 1.f, .08f);

    draw->AddText(GetIO().Fonts->Fonts[1], GetIO().Fonts->Fonts[1]->FontSize, pos + ImVec2(85 - GetIO().Fonts->Fonts[1]->CalcTextSizeA(GetIO().Fonts->Fonts[1]->FontSize, FLT_MAX, 0, "VECTRA").x / 2, 20), GetColorU32(ImGuiCol_Text), "VECTRA");
    draw->AddLine(pos + ImVec2(0, size.y - 62), pos + ImVec2(170, size.y - 62), GetColorU32(ImGuiCol_Border));
    draw->AddText(pos + ImVec2(15, size.y - 51), gui.accent_color.to_im_color(), state.status_title);
    draw->AddText(pos + ImVec2(15, size.y - 33), gui.text_disabled.to_im_color(), (std::string("v") + state.version + " / CS2 " + state.build).c_str());
    draw->AddText(pos + ImVec2(15, size.y - 17), gui.text_disabled.to_im_color(), "PRIVATE MATCH ONLY");

    SetCursorPos(ImVec2(10, 70));
    BeginChild("##tabs", ImVec2(150, size.y - 145));
    gui.group_title("Modules");
    const char* labels[] = { "Aim", "Visuals", "Config", "System" };
    const char* icons[] = { ICON_FA_CROSSHAIRS, ICON_FA_USER, ICON_FA_SAVE, ICON_FA_COG };
    for (int i = 0; i < 4; ++i) {
        if (gui.tab(icons[i], labels[i], active_tab == i) && active_tab != i) { active_tab = i; gui.m_anim = 0.f; waiting_for_key = false; }
    }
    EndChild();

    SetCursorPos(ImVec2(185, 20));
    if (Button(ICON_FA_SAVE " Save", ImVec2(74, 25))) command(VectraMenuCommand::SaveConfiguration);
    SetCursorPos(ImVec2(268, 20));
    SetNextItemWidth(145);
    InputTextWithHint("Search", "Filter controls...", search_text, sizeof(search_text));

    PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(0, 0));
    SetCursorPos(ImVec2(422, 20));
    BeginChild("##subtabs", ImVec2(203, 25));
    GetWindowDrawList()->AddRectFilled(GetWindowPos(), GetWindowPos() + GetWindowSize(), gui.button.to_im_color(), 4);
    GetWindowDrawList()->AddRect(GetWindowPos(), GetWindowPos() + GetWindowSize(), gui.border.to_im_color(), 4);
    const auto& current_subtabs = subtabs[active_tab];
    for (int i = 0; i < static_cast<int>(current_subtabs.size()); ++i) {
        if (gui.subtab(current_subtabs[i], active_subtab[active_tab] == i, static_cast<int>(current_subtabs.size()), i == 0 ? ImDrawFlags_RoundCornersLeft : i == static_cast<int>(current_subtabs.size()) - 1 ? ImDrawFlags_RoundCornersRight : 0)) {
            active_subtab[active_tab] = i; gui.m_anim = 0.f; waiting_for_key = false;
        }
        if (i + 1 < static_cast<int>(current_subtabs.size())) SameLine();
    }
    EndChild();
    PopStyleVar();

    SetCursorPos(ImVec2(632, 20));
    if (Button("_", ImVec2(23, 25))) ShowWindow(window_handle, SW_MINIMIZE);
    SameLine();
    if (Button("X", ImVec2(23, 25))) PostMessage(window_handle, WM_CLOSE, 0, 0);

    PushStyleVar(ImGuiStyleVar_Alpha, gui.m_anim);
    PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(8, 8));
    SetCursorPos(ImVec2(185, 76 - 5 * gui.m_anim));
    BeginChild("##content", ImVec2(size.x - 200, size.y - 91));
    switch (active_tab) { case 0: render_aim(); break; case 1: render_visuals(); break; case 2: render_config(); break; default: render_system(); break; }
    EndChild();
    PopStyleVar(2);
    End();
    PopStyleVar();
}
}

extern "C" __declspec(dllexport) std::int32_t __cdecl RunVectraMenu(const VectraMenuHostApi* api) {
    if (!api || api->api_version != VECTRA_MENU_API_VERSION || !api->get_state || !api->set_bool || !api->set_int || !api->set_double || !api->set_string || !api->execute_command) return 2;
    host = api;
    WNDCLASSEXW wc{ sizeof(wc), CS_CLASSDC, wnd_proc, 0, 0, GetModuleHandleW(nullptr), nullptr, LoadCursor(nullptr, IDC_ARROW), nullptr, nullptr, L"VectraNativeMenu", nullptr };
    RegisterClassExW(&wc);
    const int x = (GetSystemMetrics(SM_CXSCREEN) - menu_width) / 2;
    const int y = (GetSystemMetrics(SM_CYSCREEN) - menu_height) / 2;
    window_handle = CreateWindowExW(WS_EX_APPWINDOW | WS_EX_LAYERED | WS_EX_TOPMOST, wc.lpszClassName, L"Vectra External", WS_POPUP, x, y, menu_width, menu_height, nullptr, nullptr, wc.hInstance, nullptr);
    if (!window_handle || !create_device(window_handle)) { cleanup_device(); if (window_handle) DestroyWindow(window_handle); UnregisterClassW(wc.lpszClassName, wc.hInstance); return 3; }
    ShowWindow(window_handle, SW_SHOWDEFAULT);
    SetWindowPos(window_handle, HWND_TOPMOST, x, y, menu_width, menu_height, SWP_SHOWWINDOW);
    UpdateWindow(window_handle);

    IMGUI_CHECKVERSION();
    CreateContext();
    auto& io = GetIO();
    io.IniFilename = nullptr;
    io.Fonts->AddFontFromMemoryTTF(museo500_binary, sizeof museo500_binary, 14);
    static const ImWchar icon_ranges[] = { ICON_MIN_FA, ICON_MAX_FA, 0 };
    ImFontConfig icon_config{}; icon_config.MergeMode = true; icon_config.PixelSnapH = true;
    io.Fonts->AddFontFromMemoryTTF(&font_awesome_binary, sizeof font_awesome_binary, 13, &icon_config, icon_ranges);
    io.Fonts->AddFontFromMemoryTTF(museo900_binary, sizeof museo900_binary, 28);
    StyleColorsDark();
    auto& style = GetStyle();
    style.WindowRounding = 7.f; style.ChildRounding = 6.f; style.FrameRounding = 4.f; style.ScrollbarRounding = 6.f;
    style.Colors[ImGuiCol_WindowBg] = ImVec4(.012f, .022f, .04f, 1.f);
    style.Colors[ImGuiCol_ChildBg] = ImVec4(0, 0, 0, 0);
    style.Colors[ImGuiCol_Border] = ImVec4(1, 1, 1, .03f);
    style.Colors[ImGuiCol_Text] = ImVec4(1, 1, 1, 1);
    style.Colors[ImGuiCol_TextDisabled] = ImVec4(.51f, .52f, .56f, 1);
    style.Colors[ImGuiCol_FrameBg] = ImVec4(.023f, .039f, .07f, 1);
    style.Colors[ImGuiCol_Button] = ImVec4(.031f, .035f, .058f, 1);
    style.Colors[ImGuiCol_ButtonHovered] = ImVec4(.05f, .054f, .078f, 1);
    style.Colors[ImGuiCol_ButtonActive] = ImVec4(.07f, .074f, .098f, 1);
    style.Colors[ImGuiCol_CheckMark] = ImVec4(.3f, .49f, 1.f, 1);
    style.Colors[ImGuiCol_SliderGrab] = ImVec4(.3f, .49f, 1.f, 1);
    ImGui_ImplWin32_Init(window_handle);
    ImGui_ImplDX9_Init(device);
    blur::device = device;

    bool done = false;
    while (!done) {
        MSG message;
        while (PeekMessage(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessage(&message); if (message.message == WM_QUIT) done = true; }
        if (done) break;
        ImGui_ImplDX9_NewFrame(); ImGui_ImplWin32_NewFrame(); NewFrame(); render_menu(); EndFrame();
        device->SetRenderState(D3DRS_ZENABLE, FALSE); device->SetRenderState(D3DRS_ALPHABLENDENABLE, FALSE); device->SetRenderState(D3DRS_SCISSORTESTENABLE, FALSE);
        device->Clear(0, nullptr, D3DCLEAR_TARGET | D3DCLEAR_ZBUFFER, D3DCOLOR_RGBA(3, 6, 11, 255), 1.f, 0);
        if (device->BeginScene() >= 0) { Render(); ImGui_ImplDX9_RenderDrawData(GetDrawData()); device->EndScene(); }
        const HRESULT result = device->Present(nullptr, nullptr, nullptr, nullptr);
        if (result == D3DERR_DEVICELOST && device->TestCooperativeLevel() == D3DERR_DEVICENOTRESET) reset_device();
    }

    command(VectraMenuCommand::Shutdown);
    ImGui_ImplDX9_Shutdown(); ImGui_ImplWin32_Shutdown(); DestroyContext(); cleanup_device(); DestroyWindow(window_handle); UnregisterClassW(wc.lpszClassName, wc.hInstance);
    host = nullptr; window_handle = nullptr;
    return 0;
}

namespace {
bool create_device(HWND hwnd) {
    d3d = Direct3DCreate9(D3D_SDK_VERSION); if (!d3d) return false;
    present.Windowed = TRUE; present.SwapEffect = D3DSWAPEFFECT_DISCARD; present.BackBufferFormat = D3DFMT_UNKNOWN; present.EnableAutoDepthStencil = TRUE; present.AutoDepthStencilFormat = D3DFMT_D16; present.PresentationInterval = D3DPRESENT_INTERVAL_ONE;
    return d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, hwnd, D3DCREATE_HARDWARE_VERTEXPROCESSING, &present, &device) >= 0;
}

void cleanup_device() { if (device) { device->Release(); device = nullptr; } if (d3d) { d3d->Release(); d3d = nullptr; } }
void reset_device() { ImGui_ImplDX9_InvalidateDeviceObjects(); if (device->Reset(&present) == D3DERR_INVALIDCALL) IM_ASSERT(false); ImGui_ImplDX9_CreateDeviceObjects(); }
LRESULT WINAPI wnd_proc(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    if (ImGui_ImplWin32_WndProcHandler(hwnd, message, wparam, lparam)) return TRUE;
    switch (message) {
    case WM_NCHITTEST: { const LRESULT hit = DefWindowProcW(hwnd, message, wparam, lparam); if (hit == HTCLIENT) { POINT point{ GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam) }; ScreenToClient(hwnd, &point); if (point.y < 14) return HTCAPTION; } return hit; }
    case WM_SYSCOMMAND: if ((wparam & 0xfff0) == SC_KEYMENU) return 0; break;
    case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(hwnd, message, wparam, lparam);
}
}
