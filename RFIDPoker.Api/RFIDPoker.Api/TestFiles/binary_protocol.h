#ifndef __BINARY_PROTOCOL_H__
#define __BINARY_PROTOCOL_H__

#include <stdint.h>
#include <stdlib.h>
#include <stdio.h>
#include <stdbool.h>

#define BINARY_STX	0xF5

typedef void (*binary_function_cb)(uint8_t *buff, size_t len);

bool binary_protocol_parse(uint8_t *buff, size_t len);
void binary_protocol_send(uint8_t *buff, size_t len);
void binary_protocol_write_raw(uint8_t *buff, size_t len);
void binary_protocol_repeat(void);
void binary_protocol_init(binary_function_cb executeCommand_cb, binary_function_cb uartWrite_cb);

#endif

