#include <stdio.h>
#include <string.h>
#include "ccittcrc.h"
#include "binary_protocol.h"

binary_function_cb executeCommand;
binary_function_cb protocolWrite;

uint8_t protocolBuff[1030];
uint16_t protocolBuffIdx;
uint16_t protocolReqLen;

uint8_t protocolBuffOut[1030];
uint16_t protocolLenOut=0;

enum
{
	WAIT4STX,
	WAIT4LEN,
	RECEIVING,
} protocolState;

void binary_protocol_repeat(void)
{
    if (protocolLenOut > 0)
    	protocolWrite(protocolBuffOut, protocolLenOut);
}

void binary_protocol_write_raw(uint8_t *buff, size_t len)
{
	protocolWrite(buff, len);
}

void binary_protocol_send(uint8_t *buff, size_t len)
{
	uint16_t crc;
    protocolLenOut = 0;

	protocolBuffOut[protocolLenOut++] = BINARY_STX;

	protocolBuffOut[protocolLenOut++] = (len + 2) & 0xff;
	protocolBuffOut[protocolLenOut++] = ((len + 2) >> 8) & 0xff;

	protocolBuffOut[protocolLenOut++] = protocolBuffOut[1] ^ 0xff;
	protocolBuffOut[protocolLenOut++] = protocolBuffOut[2] ^ 0xff;

	memcpy(&protocolBuffOut[protocolLenOut], buff, len);
	protocolLenOut+=len;

	crc = GetCCITTCRC(buff, len);

	protocolBuffOut[protocolLenOut++] = (uint8_t)(crc & 0x00FF);
    protocolBuffOut[protocolLenOut++] = (uint8_t)((crc >> 8) & 0x00FF);

    protocolWrite(protocolBuffOut, protocolLenOut);
}

bool binary_protocol_parse(uint8_t *buff, size_t len)
{
	size_t k;
    uint8_t cmd;
    bool res = false;

	for (k=0; k< len; k++)
	{
		//printf("=> %02X - state 0x%02X\n", buff[k], protocolState);
		switch (protocolState)
		{
		case WAIT4STX:
			protocolBuffIdx = 0;
			if (buff[k] == BINARY_STX)
			{
				protocolState = WAIT4LEN;
			}
			break;
		case WAIT4LEN:
			protocolBuff[protocolBuffIdx++] = buff[k];
			if (protocolBuffIdx == 4)
			{
                if (protocolBuff[0] == (protocolBuff[2] ^ 0xff) && protocolBuff[1] == (protocolBuff[3] ^ 0xff))
                {
                    protocolReqLen = protocolBuff[0] | (protocolBuff[1] << 8);
                    protocolState = RECEIVING;
                    protocolBuffIdx = 0;
				}
				else
				{
                    protocolState = WAIT4STX;
                    cmd = 0xff; //protocol error
                    binary_protocol_send(&cmd, 1);
				}
			}
			break;
		case RECEIVING:
			protocolBuff[protocolBuffIdx++] = buff[k];
			if (protocolBuffIdx == protocolReqLen)
			{
				protocolState = WAIT4STX;
				if (GetCCITTCRC(protocolBuff, protocolBuffIdx - 2) != (uint16_t)(protocolBuff[protocolBuffIdx-2]) + (uint16_t)(protocolBuff[protocolBuffIdx-1] << 8))
				{
                    cmd = 0xff; //protocol error
                    binary_protocol_send(&cmd, 1);
					break;
				}
				res = true; //full correct frame received
				executeCommand(protocolBuff, protocolBuffIdx-2);
			}
			break;
		}
		//printf("=> state 0x%02X\n", protocolState);
	}

	return res;
}

void binary_protocol_init(binary_function_cb executeCommand_cb, binary_function_cb uartWrite_cb)
{
    executeCommand = executeCommand_cb;
    protocolWrite = uartWrite_cb;

	protocolState = WAIT4STX;
	protocolBuffIdx=0;
}
