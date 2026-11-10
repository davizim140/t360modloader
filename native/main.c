#include <dirent.h>
#include <errno.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "xsdk/gpu.h"
#include "xsdk/kernel.h"
#include "xsdk/log.h"
#include "xsdk/present.h"
#include "xsdk/splash.h"
#include "xsdk/text.h"

enum {
    FRAME_TIMEOUT_MS = 5000,
    BUFFER_COUNT = 2,
    SWAP_INTERVAL = 1,
    CLEAR_COLOR = 0xFF101820,
    WHITE = 0xFFF2F5F7,
    MUTED = 0xFF9BAAB5,
    GREEN = 0xFF7CDA42,
    SELECTED = 0xFFB8F28A,
    STORAGE_MAX_ENTRIES = 8,
    STORAGE_NAME_BYTES = 48,
};

typedef enum {
    PAGE_MENU,
    PAGE_DIAGNOSTICS,
    PAGE_ABOUT,
    PAGE_STORAGE,
} Page;

typedef struct {
    char name[STORAGE_NAME_BYTES];
    bool is_directory;
} StorageEntry;

static const char *const menu_items[] = {
    "Diagnostico do console",
    "Sobre o T360 ModLoader",
    "Armazenamento (somente leitura)",
    "Sair",
};

typedef struct { const char *label; const char *root; } StorageDevice;
static const StorageDevice storage_devices[] = {
    {"USB", "usb:\\"}, {"USB1", "usb1:\\"}, {"USB2", "usb2:\\"},
    {"HDD", "hdd:\\"}, {"MU", "mu:\\"}, {"MU1", "mu1:\\"}, {"DVD", "dvd:\\"}
};
#define DEVICE_TOTAL (sizeof(storage_devices) / sizeof(storage_devices[0]))
static uint8_t device_present[DEVICE_TOTAL];
static uint32_t device_count, device_index, storage_count, storage_selected;
static StorageEntry storage_entries[STORAGE_MAX_ENTRIES];
static int storage_error;
static bool storage_more;

static void copy_bounded(char *dst, size_t cap, const char *src)
{
    if (!cap) return;
    size_t n = strlen(src);
    if (n >= cap) n = cap - 1;
    memcpy(dst, src, n);
    dst[n] = '\0';
}

static int current_device_index(void)
{
    uint32_t seen = 0;
    for (uint32_t i = 0; i < DEVICE_TOTAL; ++i) {
        if (!device_present[i]) continue;
        if (seen++ == device_index) return (int)i;
    }
    return -1;
}

static void load_current_device(void)
{
    storage_count = 0;
    storage_selected = 0;
    storage_error = 0;
    storage_more = false;
    int index = current_device_index();
    if (index < 0) return;
    DIR *dir = opendir(storage_devices[index].root);
    if (!dir) { storage_error = errno; return; }
    struct dirent *entry;
    while ((entry = readdir(dir)) != NULL) {
        if (!strcmp(entry->d_name, ".") || !strcmp(entry->d_name, "..")) continue;
        if (storage_count == STORAGE_MAX_ENTRIES) { storage_more = true; break; }
        StorageEntry *item = &storage_entries[storage_count++];
        copy_bounded(item->name, sizeof(item->name), entry->d_name);
        item->is_directory = entry->d_type == DT_DIR;
    }
    (void)closedir(dir);
}

static void scan_storage_devices(void)
{
    memset(device_present, 0, sizeof(device_present));
    device_count = 0;
    device_index = 0;
    for (uint32_t i = 0; i < DEVICE_TOTAL; ++i) {
        DIR *dir = opendir(storage_devices[i].root);
        if (!dir) continue;
        (void)closedir(dir);
        device_present[i] = 1;
        ++device_count;
        xsdk_log_write("T360: detected %s at %s",
            storage_devices[i].label, storage_devices[i].root);
    }
    xsdk_log_write("T360: device scan finished: %u accessible device(s)", (unsigned)device_count);
    load_current_device();
}

static void select_device(int direction)
{
    if (device_count < 2) return;
    if (direction > 0) device_index = (device_index + 1) % device_count;
    else device_index = device_index == 0 ? device_count - 1 : device_index - 1;
    load_current_device();
}
static void draw_line(int32_t x, int32_t y, uint32_t color, const char *text)
{
    xsdk_text_draw(x, y, 2, color, text);
}

static void draw_storage(uint32_t height)
{
    draw_line(92, 250, SELECTED, "DISPOSITIVOS CONECTADOS");
    xsdk_text_draw(92, 292, 1, MUTED, "Deteccao automatica - somente leitura");
    if (!device_count) {
        xsdk_text_draw(92, 370, 2, WHITE, "Nenhuma unidade acessivel detectada.");
        xsdk_text_draw(92, 414, 1, MUTED, "Conecte um dispositivo e pressione A para atualizar.");
    } else {
        int active = current_device_index();
        if (active >= 0) {
            xsdk_text_draw(92, 334, 2, SELECTED, storage_devices[active].label);
            xsdk_text_draw(250, 334, 1, MUTED, storage_devices[active].root);
        }
        xsdk_text_draw(92, 372, 1, MUTED, "Unidades encontradas:");
        int32_t y = 400;
        for (uint32_t i = 0; i < DEVICE_TOTAL; ++i) {
            if (!device_present[i]) continue;
            char label[32];
            const char *name = storage_devices[i].label;
            unsigned p = 0;
            label[p++] = (active == (int)i) ? '>' : ' ';
            label[p++] = ' ';
            while (*name && p < sizeof(label) - 1) label[p++] = *name++;
            label[p] = '\0';
            xsdk_text_draw(112, y, 1, active == (int)i ? SELECTED : WHITE, label);
            y += 25;
        }
        xsdk_text_draw(360, 372, 1, MUTED, "Itens na raiz:");
        if (storage_error) {
            xsdk_text_draw(360, 410, 1, WHITE, "Falha ao ler unidade.");
        } else if (!storage_count) {
            xsdk_text_draw(360, 410, 1, WHITE, "Unidade vazia.");
        } else {
            for (uint32_t i = 0; i < storage_count && i < 5; ++i) {
                char line[STORAGE_NAME_BYTES + 4];
                unsigned p = 0;
                line[p++] = i == storage_selected ? '>' : ' ';
                line[p++] = ' ';
                const char *name = storage_entries[i].name;
                while (*name && p < sizeof(line) - 2) line[p++] = *name++;
                if (storage_entries[i].is_directory && p < sizeof(line) - 1) line[p++] = '/';
                line[p] = '\0';
                xsdk_text_draw(380, 402 + (int32_t)i * 27, 1,
                    i == storage_selected ? SELECTED : WHITE, line);
            }
            if (storage_more) xsdk_text_draw(380, 540, 1, MUTED, "Mais itens...");
        }
    }
    xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED,
        "ESQ/DIR: trocar unidade   A: detectar novamente   B: voltar");
}

static void draw_screen(uint32_t width, uint32_t height, Page page, uint32_t selected,
    uint32_t controller_status)
{
    uint32_t address = 0;
    const NTSTATUS status = xsdk_present_begin(&address, FRAME_TIMEOUT_MS);
    if (!NT_SUCCESS(status)) {
        xsdk_log_write("T360: xsdk_present_begin failed: 0x%08X", (unsigned)status);
        return;
    }

    xsdk_gpu_set_target(width, height);
    draw_line(72, 54, GREEN, "T360 MODLOADER");
    draw_line(72, 104, WHITE, "Gerenciador nativo para Xbox 360 RGH");
    xsdk_text_draw(72, 166, 1, MUTED, "Versao inicial - interface e entrada do controle");

    if (page == PAGE_MENU) {
        xsdk_text_draw(92, 250, 2, MUTED, "MENU PRINCIPAL");
        for (uint32_t i = 0; i < sizeof menu_items / sizeof menu_items[0]; ++i) {
            const uint32_t color = i == selected ? SELECTED : WHITE;
            char line[96];
            const char *prefix = i == selected ? "> " : "  ";
            const char *label = menu_items[i];
            unsigned j = 0;
            while (prefix[j] && j < sizeof line - 1) {
                line[j] = prefix[j];
                ++j;
            }
            unsigned k = 0;
            while (label[k] && j < sizeof line - 1) {
                line[j++] = label[k++];
            }
            line[j] = '\0';
            xsdk_text_draw(112, 316 + (int32_t)i * 58, 2, color, line);
        }
        xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED,
            "D-PAD: navegar   A: selecionar   BACK: sair");
    } else if (page == PAGE_DIAGNOSTICS) {
        draw_line(92, 250, SELECTED, "DIAGNOSTICO");
        xsdk_text_draw(92, 316, 2, WHITE,
            controller_status == ERROR_SUCCESS ? "Controle: conectado" : "Controle: nao detectado");
        xsdk_text_draw(92, 370, 2, WHITE, "Video: modo 720p inicializado");
        xsdk_text_draw(92, 424, 2, WHITE, "Interface: renderizacao ativa");
        xsdk_text_draw(92, 500, 1, MUTED, "Este teste nao altera arquivos do console.");
        xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED, "B ou BACK: voltar");
    } else if (page == PAGE_ABOUT) {
        draw_line(92, 250, SELECTED, "SOBRE");
        xsdk_text_draw(92, 316, 2, WHITE, "T360 ModLoader");
        xsdk_text_draw(92, 370, 2, WHITE, "Projeto comunitario para Terraria Xbox 360.");
        xsdk_text_draw(92, 424, 2, WHITE, "O carregamento de mods ainda nao esta implementado.");
        xsdk_text_draw(92, 478, 2, WHITE, "Nenhum arquivo do Terraria e modificado.");
        xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED, "B ou BACK: voltar");
    } else {
        draw_storage(height);
    }

    xsdk_gpu_resolve(address, width, height, CLEAR_COLOR);
    xsdk_present_end();
}

int main(void)
{
    xsdk_log_write("T360: native manager startup");
    const NTSTATUS splash = xsdk_splash_show(1200);
    if (!NT_SUCCESS(splash))
        xsdk_log_write("T360: splash unavailable: 0x%08X", (unsigned)splash);

    NTSTATUS status = xsdk_gpu_open();
    if (!NT_SUCCESS(status)) {
        xsdk_log_write("T360: GPU initialization failed: 0x%08X", (unsigned)status);
        return 1;
    }

    uint32_t width = 0;
    uint32_t height = 0;
    status = xsdk_present_open(XSDK_DISPLAY_720P, BUFFER_COUNT, SWAP_INTERVAL, &width, &height);
    if (!NT_SUCCESS(status)) {
        xsdk_log_write("T360: display initialization failed: 0x%08X", (unsigned)status);
        xsdk_gpu_close();
        return 1;
    }

    status = xsdk_text_open();
    if (!NT_SUCCESS(status)) {
        xsdk_log_write("T360: text initialization failed: 0x%08X", (unsigned)status);
        xsdk_present_close();
        xsdk_gpu_close();
        return 1;
    }

    xsdk_log_write("T360: display ready: %ux%u", (unsigned)width, (unsigned)height);

    Page page = PAGE_MENU;
    uint32_t selected = 0;
    uint16_t previous_buttons = 0;
    bool running = true;

    while (running) {
        INPUT_STATE input = { 0 };
        const uint32_t controller_status = XamInputGetState(0, 0, &input);
        if (controller_status != ERROR_SUCCESS)
            input = (INPUT_STATE){ 0 };

        const uint16_t buttons = input.gamepad.buttons;
        const uint16_t pressed = buttons & (uint16_t)~previous_buttons;
        previous_buttons = buttons;

        if (pressed & INPUT_GAMEPAD_BACK) {
            if (page == PAGE_MENU)
                running = false;
            else
                page = PAGE_MENU;
        } else if (page == PAGE_MENU) {
            if (pressed & INPUT_GAMEPAD_DPAD_UP)
                selected = selected == 0 ? 3 : selected - 1;
            if (pressed & INPUT_GAMEPAD_DPAD_DOWN)
                selected = (selected + 1) % 4;
            if (pressed & INPUT_GAMEPAD_A) {
                if (selected == 0)
                    page = PAGE_DIAGNOSTICS;
                else if (selected == 1)
                    page = PAGE_ABOUT;
                else if (selected == 2) {
                    page = PAGE_STORAGE;
                    scan_storage_devices();
                } else {
                    running = false;
                }
            }
        } else if (page == PAGE_STORAGE) {
            if (pressed & INPUT_GAMEPAD_DPAD_LEFT) select_device(-1);
            if (pressed & INPUT_GAMEPAD_DPAD_RIGHT) select_device(1);
            if (pressed & INPUT_GAMEPAD_A) scan_storage_devices();
            if (storage_count > 0 && (pressed & INPUT_GAMEPAD_DPAD_UP))
                storage_selected = storage_selected == 0 ? storage_count - 1 : storage_selected - 1;
            if (storage_count > 0 && (pressed & INPUT_GAMEPAD_DPAD_DOWN))
                storage_selected = (storage_selected + 1) % storage_count;
            if (pressed & INPUT_GAMEPAD_B)
                page = PAGE_MENU;
        } else if (pressed & INPUT_GAMEPAD_B) {
            page = PAGE_MENU;
        }

        draw_screen(width, height, page, selected, controller_status);
    }

    xsdk_text_close();
    xsdk_present_close();
    status = xsdk_gpu_close();
    if (!NT_SUCCESS(status))
        xsdk_log_write("T360: GPU shutdown warning: 0x%08X", (unsigned)status);
    xsdk_log_write("T360: clean exit");
    return 0;
}
