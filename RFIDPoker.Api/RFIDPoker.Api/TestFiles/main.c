/**
 *
 * @file      main.c
 * @brief     Application to test  C1
 * @author    Marcin Baliniak
 * @date      08/05/2017
 * @copyright Eccel Technology Ltd
 */

#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include<string.h>
#include <unistd.h>
#include <termios.h>
#include <errno.h>
#include <fcntl.h>
#include <signal.h>
#include <time.h>
#include<sys/socket.h>
#include<arpa/inet.h>
#include <ctype.h>
#include <netdb.h>
#include <sys/types.h>
#include <netinet/in.h>

#include "binary_protocol.h"
#include "commands_binary.h"

#define MAX_FRAME_SIZE 2048

#define TEST_SSID     "your-ssid"
#define TEST_PASSWORD "your-wifi-password"

/*
changelog:
Version 1.1 
    - fixed Desfire example
    - extra logs for timeout communication
    - extended help


*/

int silent_mode = 0;


int serial_fd = -1;
int std_output_fd = 1;

int own_printf(const char *format, ...)
{
    char buff[4096];
    int len;

    va_list args;
    va_start( args, format );
    len = vsnprintf(buff, sizeof(buff), format, args );

    if (std_output_fd == 1)
    {
        write(std_output_fd, buff, len);
    }
    else if(send(std_output_fd, buff, len, MSG_DONTWAIT)<0)
    {
        perror("\nSending failed.Error:");
    }
    va_end( args );
    return len;
}

/**
    @brief Function used to open UART port
    @param[in] device - patch to devive
    @param[in] baudRate - selected baud rate
    @return device descriptor
    @return -1 if application can't open /dev/serial0 device
    @return -2 if incompatible baud is selected
*/
static int open_port(char *device, uint32_t baudRate)
{
    int uart_fd = -1;
    struct termios options;
    tcflag_t baud;

    uart_fd = open(device, O_RDWR | O_NOCTTY | O_NDELAY);		//Open in non blocking read/write mode,
    //serial0 is always link to UART on GPIO header
    if (uart_fd == -1)
    {
        own_printf("Error - Unable to open UART.  Ensure it is not in use by another application\n");
        return -1;
    }

    switch (baudRate)
    {
    case 1200:      baud = B1200; break;
    case 2400:      baud = B2400; break;
    case 4800:      baud = B4800; break;
    case 9600:      baud = B9600; break;
    case 19200:     baud = B19200;break;
    case 38400:     baud = B38400;break;
    case 57600:     baud = B57600;break;
    case 115200:    baud = B115200;break;
    case 230400:    baud = B230400;break;
    case 460800:    baud = B460800;break;
    case 921600:    baud = B921600;break;
    default:
        return -2;
    }

    tcgetattr(uart_fd, &options);
    options.c_cflag = baud | CS8 | CLOCAL | CREAD;        //<Set baud rate
    options.c_iflag = IGNPAR;
    options.c_oflag = 0;
    options.c_lflag = 0;
    tcflush(uart_fd, TCIFLUSH);
    tcsetattr(uart_fd, TCSANOW, &options);

    return uart_fd;
}

/**
    @brief Function used to open TCP socket
    @param[in] address + port string
    @return device descriptor
    @return -1 if application can't open TCP socket
*/
static int open_socket(char *address)
{
    int sock_fd = -1, port;
    struct hostent *he;
    struct sockaddr_in their_addr; /* connector's address information */
    char *sport;

    sport = strstr(address, ":");
    *sport = 0;
    sport++;
    port = atoi(sport);

    own_printf("Trying to connect to host %s, port %d\n", address, port);


    if ((he=gethostbyname(address)) == NULL) {  /* get the host info */
        herror("gethostbyname");
        exit(1);
    }

    if ((sock_fd = socket(AF_INET, SOCK_STREAM, 0)) == -1) {
        perror("socket");
        exit(1);
    }

    their_addr.sin_family = AF_INET;      /* host byte order */
    their_addr.sin_port = htons(port);    /* short, network byte order */
    their_addr.sin_addr = *((struct in_addr *)he->h_addr);
    bzero(&(their_addr.sin_zero), 8);     /* zero the rest of the struct */

    if (connect(sock_fd, (struct sockaddr *)&their_addr, \
                                          sizeof(struct sockaddr)) == -1) {
        perror("connect");
        return -1;
    }

    return sock_fd;
}


/**
    @brief Function used to send prepared data to UART hardware
    @param[in] data - data
    @param[in] size - data size
    @details Function is called after every UART command.
*/
void uart_protocol_write(uint8_t *data, size_t size)
{
    if (0)
    {
        int i;
        own_printf("uart_protocol_write %d bytes\n", size);
        for (i = 0; i < size; i++)
            own_printf(" 0x%02X", data[i]);
        own_printf("\n");
    }


    if (write(serial_fd, data, size) <  size)
        own_printf("uart_protocol_write error writing!\n");
}

void mifare_commands_execute(uint8_t *buff, size_t len)
{
    uint8_t cmd[4096];
    uint32_t val32;
    static uint8_t tag_count;

    srand(time(0));

    if (buff[0] == CMD_ERROR)
    {
        own_printf( "Command 0x%02X failed with ERROR 0x%02X%02X!!!\n", buff[1], buff[2], buff[3]);
    }
    else if (buff[0] == CMD_ACK)
    switch (buff[1])
    {
        case CMD_DUMMY_COMMAND:
            own_printf( "OK\n");
            cmd[0] = CMD_GET_TAG_COUNT;
            binary_protocol_send(cmd, 1);
            own_printf( "==> Get tag count = ");
        break;
        case CMD_GET_TAG_COUNT:
            tag_count = buff[2];
            own_printf( "%d\n", tag_count);
            if (tag_count > 0)
            {
                cmd[0] = CMD_GET_UID;
                cmd[1] = tag_count-1;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Get UID info");
            }
            else
            {
                cmd[0] = CMD_SET_POLLING;
                cmd[1] = 1;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Enable polling - ");
            }
        break;
        case CMD_GET_UID:
            own_printf("type: 0x%02X, param: 0x%02X, UID len: 0x%02X, UID: ", buff[2], buff[3], len-4);
            for (uint8_t k=0; k< len-4; k++)
                own_printf( " 0x%02X", buff[k+4]);
            own_printf( "\n");

            cmd[0] = CMD_ACTIVATE_TAG;
            cmd[1] = tag_count-1;
            binary_protocol_send(cmd, 2);
            own_printf( "==> Activate tag %d - ", tag_count-1);
        break;
        case CMD_ACTIVATE_TAG:
            own_printf( "OK\n");

            cmd[0] = CMD_SET_KEY;
            cmd[1] = 0;
            cmd[2] = KEY_TYPE_MIFARE;
            memset(&cmd[3], 0xff, 12); //keyA + keyB

            binary_protocol_send(cmd, 15);
            own_printf( "==> Set key 0 to 0x%02X%02X...", cmd[3], cmd[4]);
        break;
        case CMD_SET_KEY:
            own_printf( "OK\n");

            cmd[0] = CMD_MF_WRITE_BLOCK;
            cmd[1] = 1;
            cmd[2] = 2;
            cmd[3] = 0x0A;
            cmd[4] = 0; //keyNo = 0

            cmd[5] = rand()%255;
            for (int k=0; k< 2*16; k++)
                cmd[6+k] = cmd[5+k]+1;

            binary_protocol_send(cmd, 37);
            own_printf( "==> Writing data to tag 0x%02X 0x%02X 0x%02X...- ", cmd[5], cmd[6], cmd[7]);
        break;
        case CMD_MF_WRITE_BLOCK:
            own_printf( "OK\n");
            cmd[0] = CMD_MF_READ_BLOCK;
            cmd[1] = 1;
            cmd[2] = 2;
            cmd[3] = 0x0A;
            cmd[4] = 0; //keyNo = 0

            binary_protocol_send(cmd, 5);
            own_printf( "==> Reading data:");
        break;
        case CMD_MF_READ_BLOCK:
            for (int k=0; k< len-1; k++)
            {
                if (k%16==0)
                    own_printf( "\n\t\t");
                own_printf( " 0x%02X", buff[k+2]);
            }
            own_printf( "\n");

            val32 = 1234;
            cmd[0] = CMD_MF_WRITE_VALUE;
            cmd[1] = 5;
            cmd[2] = 0x0A;
            cmd[3] = 0; //keyNo = 0
            memcpy(&cmd[4], &val32, 4);
            cmd[8] = 55;

            binary_protocol_send(cmd, 9);
            own_printf( "==> Writing value - %d, addess %d - ", val32, cmd[8]);
        break;
        case CMD_MF_WRITE_VALUE:
            own_printf( "OK\n");

            val32 = 5;
            cmd[0] = CMD_MF_INCREMENT;
            cmd[1] = 5;
            cmd[2] = 0x0A;
            cmd[3] = 0; //keyNo = 0
            memcpy(&cmd[4], &val32, 4);
            cmd[8] = 0x01; //increment

            binary_protocol_send(cmd, 9);
            own_printf( "==> Increment value by %d - ", val32);
        break;
        case CMD_MF_INCREMENT:
            own_printf( "OK\n");
            cmd[0] = CMD_MF_TRANSFER;
            cmd[1] = 5;
            cmd[2] = 0x0A;
            cmd[3] = 0; //keyNo = 0

            binary_protocol_send(cmd, 4);
            own_printf( "==> Transfer value - ");
        break;
        case CMD_MF_TRANSFER:
            own_printf( "OK\n");
            cmd[0] = CMD_MF_READ_VALUE;
            cmd[1] = 5;
            cmd[2] = 0x0A;
            cmd[3] = 0; //keyNo = 0

            binary_protocol_send(cmd, 4);
            own_printf( "==> Reading value - ");
        break;
        case CMD_MF_READ_VALUE:
            own_printf( "OK\n");
            cmd[0] = CMD_SET_POLLING;
            cmd[1] = 1;
            binary_protocol_send(cmd, 2);
            own_printf( "==> Enable polling - ");
        break;
        case CMD_SET_POLLING:
            own_printf( "OK\n");
            own_printf( "Test finished!\n");
            exit(0);
        break;
    }
}


void mifare_ul_commands_execute(uint8_t *buff, size_t len)
{
    uint8_t cmd[4096];
    uint32_t val32;
    static uint8_t tag_count;

    srand(time(0));

    if (buff[0] == CMD_ERROR)
    {
        own_printf( "Command 0x%02X failed with ERROR 0x%02X%02X!!!\n", buff[1], buff[2], buff[3]);
    }
    else if (buff[0] == CMD_ACK)
    switch (buff[1])
    {
        case CMD_DUMMY_COMMAND:
            own_printf( "OK\n");
            cmd[0] = CMD_GET_TAG_COUNT;
            binary_protocol_send(cmd, 1);
            own_printf( "==> Get tag count = ");
        break;
        case CMD_GET_TAG_COUNT:
            tag_count = buff[2];
            own_printf( "%d\n", tag_count);
            if (tag_count > 0)
            {
                cmd[0] = CMD_GET_UID;
                cmd[1] = tag_count-1;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Get UID info");
            }
            else
            {
                cmd[0] = CMD_SET_POLLING;
                cmd[1] = 1;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Enable polling - ");
            }
        break;
        case CMD_GET_UID:
            own_printf("type: 0x%02X, param: 0x%02X, UID len: 0x%02X, UID: ", buff[2], buff[3], len-4);
            for (uint8_t k=0; k< len-4; k++)
                own_printf( " 0x%02X", buff[k+4]);
            own_printf( "\n");

            cmd[0] = CMD_ACTIVATE_TAG;
            cmd[1] = tag_count-1;
            binary_protocol_send(cmd, 2);
            own_printf( "==> Activate tag %d - ", tag_count-1);
        break;
        case CMD_ACTIVATE_TAG:
            own_printf( "OK\n");

            cmd[0] = CMD_MFU_WRITE_PAGE;
            cmd[1] = 4;
            cmd[2] = 2;

            cmd[3] = rand()%255;
            for (int k=0; k< 2*4; k++)
                cmd[4+k] = cmd[3+k]+1;

            binary_protocol_send(cmd, 11);
            own_printf( "==> Writing data to tag 0x%02X 0x%02X 0x%02X...- ", cmd[3], cmd[4], cmd[5]);
        break;
        case CMD_MFU_WRITE_PAGE:
            own_printf( "OK\n");
            cmd[0] = CMD_MFU_READ_PAGE;
            cmd[1] = 4;
            cmd[2] = 2;

            binary_protocol_send(cmd, 3);
            own_printf( "==> Reading data:");
        break;
        case CMD_MFU_READ_PAGE:
            for (int k=0; k< len-1; k++)
            {
                if (k%16==0)
                    own_printf( "\n\t\t");
                own_printf( " 0x%02X", buff[k+2]);
            }
            own_printf( "\n");

            cmd[0] = CMD_MFU_GET_VERSION;

            binary_protocol_send(cmd, 1);
            own_printf( "==> Reading version - ");
        break;
        case CMD_MFU_GET_VERSION:
            for (int k=0; k< len-1; k++)
            {
                own_printf( " 0x%02X", buff[k+2]);
            }
            own_printf( "\n");

            cmd[0] = CMD_MFU_READ_SIG;

            binary_protocol_send(cmd, 1);
            own_printf( "==> Get signature - ");
        break;
        case CMD_MFU_READ_SIG:
            for (int k=0; k< len-1; k++)
            {
                if (k%16==0)
                    own_printf( "\n\t\t");
                own_printf( " 0x%02X", buff[k+2]);
            }
            own_printf( "\n");
            cmd[0] = CMD_MFU_READ_COUNTER;
            cmd[1] = 1;

            binary_protocol_send(cmd, 2);
            own_printf( "==> Reading counter - ");
        break;
        case CMD_MFU_READ_COUNTER:
            for (int k=0; k< len-2; k++)
            {
                own_printf( " 0x%02X", buff[k+2]);
            }
            own_printf( "\n");
            cmd[0] = CMD_MFU_INCREMENT_COUNTER;
            cmd[1] = 1; //counter number
            cmd[2] = 1; //value
            cmd[3] = 0;
            cmd[4] = 0;

            binary_protocol_send(cmd, 5);
            own_printf( "==> Incrementing counter - ");
        break;
        case CMD_MFU_INCREMENT_COUNTER:
            own_printf( "OK\n");
            cmd[0] = CMD_SET_POLLING;
            cmd[1] = 1;
            binary_protocol_send(cmd, 2);
            own_printf( "==> Enable polling - ");
        break;
        case CMD_SET_POLLING:
            own_printf( "OK\n");
            own_printf( "Test finished\n");
            exit(0);
        break;
    }
}

void mifare_df_commands_execute(uint8_t *buff, size_t len)
{
    uint8_t cmd[4096];
    uint32_t val32;
    uint16_t val16;
    int32_t ival32;
    static uint8_t tag_count;
    static uint8_t idle_count;
    static uint8_t operation_to_commit;


	struct {
		uint16_t nr;
		char text[32];
	} test_record;

    srand(time(0));

    if (buff[0] == CMD_ERROR)
    {
        own_printf( "Command 0x%02X failed with ERROR 0x%02X%02X!!!\n", buff[1], buff[2], buff[3]);
        exit(-1);
    }
    else if (buff[0] == CMD_ACK)
    switch (buff[1])
    {
        case CMD_DUMMY_COMMAND:
            own_printf( "OK\n");
            cmd[0] = CMD_GET_TAG_COUNT;
            binary_protocol_send(cmd, 1);
            own_printf( "==> Get tag count = ");
        break;
        case CMD_GET_TAG_COUNT:
            tag_count = buff[2];
            own_printf( "%d\n", tag_count);
            if (tag_count > 0)
            {
                cmd[0] = CMD_GET_UID;
                cmd[1] = tag_count-1;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Get UID info");
            }
            else
            {
                cmd[0] = CMD_SET_POLLING;
                cmd[1] = 1;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Enable polling - ");
            }
        break;
        case CMD_GET_UID:
            own_printf("type: 0x%02X, param: 0x%02X, UID len: 0x%02X, UID: ", buff[2], buff[3], len-4);
            for (uint8_t k=0; k< len-4; k++)
                own_printf( " 0x%02X", buff[k+4]);
            own_printf("\n");

            if (buff[3] == 0x20)
            {
                own_printf( "Desfire tag detected, performing test...\n");
                idle_count = 0;

                cmd[0] = CMD_SET_KEY;
                cmd[1] = 0; //key no
                cmd[2] = KEY_TYPE_DES; //key type

                memset(&cmd[3], 0x00, 16);
                binary_protocol_send(cmd, 19);
                own_printf( "==> Set key in storage no %d\n", idle_count);
            }
            else
            {
                own_printf("\nIt is not Desfire tag, exiting...\n");
                exit(-1);
            }
        break;
        case CMD_SET_KEY:
            if (idle_count < 2)
            {
                cmd[0] = CMD_SET_KEY;
                cmd[1] = idle_count+1; //key no
                cmd[2] = KEY_TYPE_AES128; //key type

                memset(&cmd[3], idle_count, 16);
                idle_count++;
                binary_protocol_send(cmd, 19);
                own_printf( "==> Set key in storage no %d\n", idle_count);
            }
            else
            {
                cmd[0] = CMD_MFDF_SELECT_APP; //select master app
                cmd[1] = 0;
                cmd[2] = 0;
                cmd[3] = 0;
                idle_count = 0;
                binary_protocol_send(cmd, 4);
                own_printf( "==> Selecting masster app - ");
            }
        break;
        case CMD_MFDF_SELECT_APP:
            own_printf( "OK\n");
            if (idle_count == 0) //master app selected
            {
                cmd[0] = CMD_MFDF_AUTH;
                cmd[1] = 0;
                cmd[2] = 0;
                cmd[3] = 0;
                idle_count = 0;
                binary_protocol_send(cmd, 4);
                own_printf( "==> Authorizing master app - ");
            }
            else
            {
                cmd[0] = CMD_MFDF_AUTH_AES;
                cmd[1] = 1;
                cmd[2] = 0;
                cmd[3] = 0;
                idle_count = 1;
                binary_protocol_send(cmd, 4);
                own_printf( "==> Authorizing test app - ");
            }
        break;
        case CMD_MFDF_AUTH:
            own_printf( "OK\n");
            if (idle_count==0)
            {
                cmd[0] = CMD_MFDF_FORMAT;
                binary_protocol_send(cmd, 1);
                own_printf( "==> Formating tag - ");
            }
        break;
        case CMD_MFDF_AUTH_AES:
            own_printf( "OK\n");
            cmd[0] = CMD_MFDF_CREATE_DATA_FILE;
            cmd[1] = 0x01;
            cmd[2] = 0xEE;
            cmd[3] = 0xEE;
            cmd[4] = 32;
            cmd[5] = 0;
            cmd[6] = 0;
            cmd[7] = 1;
            binary_protocol_send(cmd, 8);
            own_printf( "==> Creating data file - ");
        break;
        case CMD_MFDF_FORMAT:
            own_printf( "OK\n");
            cmd[0] = CMD_MFDF_GET_FREEMEM;
            binary_protocol_send(cmd, 1);
            own_printf( "==> Get free memory - ");
        break;
        case CMD_MFDF_GET_FREEMEM:
            own_printf( "%d bytes\n", *((uint32_t *)&buff[2]));
            cmd[0] = CMD_MFDF_GET_VERSION;
            binary_protocol_send(cmd, 1);
            own_printf( "==> Get version - ");
        break;
        case CMD_MFDF_GET_VERSION:
            for (int k = 0; k < 28; k++)
                own_printf( "%02X ", buff[2+k]);
            own_printf( "\n");

            cmd[0] = CMD_MFDF_CREATE_APP;
            cmd[1] = 0xAA;
            cmd[2] = 0x55;
            cmd[3] = 0xAA;
            cmd[4] = 0xED;
            cmd[5] = 0x84;

            binary_protocol_send(cmd, 6);
            own_printf( "==> Creating new app  - ");
        break;
        case CMD_MFDF_CREATE_APP:
            own_printf( "OK\n");
            cmd[0] = CMD_MFDF_SELECT_APP;
            cmd[1] = 0xAA;
            cmd[2] = 0x55;
            cmd[3] = 0xAA;
            idle_count = 1;
            binary_protocol_send(cmd, 4);
            own_printf( "==> Selecting test app - ");
        break;
        case CMD_MFDF_CREATE_DATA_FILE:
            own_printf( "OK\n");
            //write to file
            cmd[0] = CMD_MFDF_WRITE_DATA;
            cmd[1] = 0x01;
            cmd[2] = 0x00;
            cmd[3] = 0x00;
            cmd[4] = 0x00;
            sprintf(&cmd[5], "Ala ma kota");
            binary_protocol_send(cmd, 17);
            own_printf( "==> Writing to data file - ");
        break;
        case CMD_MFDF_WRITE_DATA:
            own_printf( "OK\n");
            cmd[0] = CMD_MFDF_COMMIT_TRANSACTION;
            operation_to_commit = CMD_MFDF_WRITE_DATA;
            binary_protocol_send(cmd, 1);
            own_printf( "==> Commit last write - ");
        break;
        case CMD_MFDF_COMMIT_TRANSACTION:
            own_printf( "OK\n");
            if (operation_to_commit == CMD_MFDF_WRITE_DATA)
            {
                cmd[0] = CMD_MFDF_READ_DATA;
                cmd[1] = 0x01; //file no
                cmd[2] = 0x00;
                cmd[3] = 0x00;
                cmd[4] = 0x00;
                cmd[5] = 12;
                cmd[6] = 0;
                cmd[7] = 0;
                binary_protocol_send(cmd, 8);
                own_printf( "==> Reading data file - ");
            }
            else if (operation_to_commit == CMD_MFDF_CREDIT || operation_to_commit == CMD_MFDF_DEBIT)
            {
                cmd[0] = CMD_MFDF_GET_VALUE;
                cmd[1] = 0x02;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Get value from file - ");
            }
            else if (operation_to_commit == CMD_MFDF_WRITE_RECORD)
            {
                if (idle_count < 12)
                {
                    cmd[0] = CMD_MFDF_WRITE_RECORD;
                    cmd[1] = 0x03;
                    test_record.nr = idle_count;
                    sprintf(test_record.text, "This is record nr %d", test_record.nr);
                    memcpy(&cmd[2], &test_record, sizeof(test_record));
                    binary_protocol_send(cmd, 2+sizeof(test_record));
                    own_printf( "==> Writing record %d - ", idle_count);
                    idle_count++;
                }
                else
                {
                    idle_count=0;
                    cmd[0] = CMD_MFDF_READ_RECORD;
                    cmd[1] = 0x03;
                    val16 = idle_count;
                    memcpy(&cmd[2], &val16, 2);
                    val16 = sizeof(test_record);
                    memcpy(&cmd[4], &val16, 2);
                    binary_protocol_send(cmd, 6);
                    own_printf( "==> Reading record %d - ", idle_count);
                }
            }
            else if (operation_to_commit == CMD_MFDF_CLEAR_RECORDS)
            {
                idle_count = 1;
                cmd[0] = CMD_MFDF_DELETE_FILE;
                cmd[1] = idle_count;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Delete file %d - ", idle_count);
            }
        break;
        case CMD_MFDF_READ_DATA:
            own_printf( "%s\n", &buff[2]);

            cmd[0] = CMD_MFDF_CREATE_VALUE_FILE;
            cmd[1] = 0x02;
            cmd[2] = 0xEE;
            cmd[3] = 0xEE;

            ival32 = -100;
            memcpy(&cmd[4], &ival32, 4);

            ival32 = 100;
            memcpy(&cmd[8], &ival32, 4);

            ival32 = -5;
            memcpy(&cmd[12], &ival32, 4);
            cmd[16] = 0x01;
            cmd[17] = 0x01;
            binary_protocol_send(cmd, 18);
            own_printf( "==> Create value file - ");
        break;
        case CMD_MFDF_CREATE_VALUE_FILE:
            own_printf( "OK\n");
            cmd[0] = CMD_MFDF_GET_VALUE;
            cmd[1] = 0x02;
            binary_protocol_send(cmd, 2);
            own_printf( "==> Read value from file - ");
            idle_count = 0;
        break;
        case CMD_MFDF_GET_VALUE:
            memcpy(&ival32, &buff[2], 4);
            own_printf( "(%d)\n", ival32);
            if (operation_to_commit == CMD_MFDF_WRITE_DATA)
            {
                cmd[0] = CMD_MFDF_CREDIT;
                cmd[1] = 0x02;
                ival32 = 10;
                memcpy(&cmd[2], &ival32, 4);
                binary_protocol_send(cmd, 6);
                own_printf( "==> Get credit 10 - ");
            }
            else if (operation_to_commit == CMD_MFDF_CREDIT)
            {
                cmd[0] = CMD_MFDF_DEBIT;
                cmd[1] = 0x02;
                ival32 = 25;
                memcpy(&cmd[2], &ival32, 4);
                binary_protocol_send(cmd, 6);
                own_printf( "==> Get debit 25 - ");
            }
            else if (operation_to_commit == CMD_MFDF_DEBIT)
            {
                cmd[0] = CMD_MFDF_CREATE_RECORD_FILE;
                cmd[1] = 0x03;
                cmd[2] = 0xEE;
                cmd[3] = 0xEE;

                val16 = sizeof(test_record);
                memcpy(&cmd[4], &val16, 2);

                val16 = 10; //number of records
                memcpy(&cmd[6], &val16, 2);

                cmd[8] = 1;
                binary_protocol_send(cmd, 9);
                own_printf( "==> Create record file - ");
                idle_count = 0;
            }
        break;
        case CMD_MFDF_CREDIT:
        case CMD_MFDF_DEBIT:
        case CMD_MFDF_WRITE_RECORD:
        case CMD_MFDF_CLEAR_RECORDS:
            own_printf( "OK\n");
            cmd[0] = CMD_MFDF_COMMIT_TRANSACTION;
            binary_protocol_send(cmd, 1);
            operation_to_commit = buff[1];
            own_printf( "==> Commit last operation - ");
        break;
        case CMD_MFDF_CREATE_RECORD_FILE:
                own_printf( "OK\n");
                cmd[0] = CMD_MFDF_WRITE_RECORD;
                cmd[1] = 0x03;
                test_record.nr = idle_count;
                sprintf(test_record.text, "This is record nr %d", test_record.nr);
                memcpy(&cmd[2], &test_record, sizeof(test_record));
                binary_protocol_send(cmd, 2+sizeof(test_record));
                own_printf( "==> Writing record %d - ", idle_count);
                idle_count++;
            break;
        case CMD_MFDF_READ_RECORD:
            idle_count++;
            memcpy(&test_record, buff+2, len -2);
            own_printf("Nr %d, data: \"%s\"\n", test_record.nr, test_record.text);
            if (idle_count < 9)
            {
                cmd[0] = CMD_MFDF_READ_RECORD;
                cmd[1] = 0x03;
                val16 = idle_count;
                memcpy(&cmd[2], &val16, 2);
                val16 = sizeof(test_record);
                memcpy(&cmd[4], &val16, 2);
                binary_protocol_send(cmd, 6);
                own_printf( "==> Reading record %d - ", idle_count);
            }
            else
            {
                cmd[0] = CMD_MFDF_CLEAR_RECORDS;
                cmd[1] = 0x03;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Clear records %d - ", idle_count);
            }
        break;
        case CMD_MFDF_DELETE_FILE:
            own_printf( "OK\n");
            idle_count++;
            if (idle_count < 3)
            {
                cmd[0] = CMD_MFDF_DELETE_FILE;
                cmd[1] = idle_count;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Delete file %d - ", idle_count);
            }
            else
            {
                cmd[0] = CMD_MFDF_DELETE_APP;
                cmd[1] = 0xAA;
                cmd[2] = 0x55;
                cmd[3] = 0xAA;
                binary_protocol_send(cmd, 4);
                own_printf( "==> Delete app %02X %02X %02X - ", cmd[1], cmd[2], cmd[3]);
            }
        break;
        case CMD_MFDF_DELETE_APP:
            own_printf( "OK\n");
            cmd[0] = CMD_SET_POLLING;
            cmd[1] = 1;
            binary_protocol_send(cmd, 2);
            own_printf( "==> Enable polling - ");
        break;
        case CMD_SET_POLLING:
            own_printf( "OK\n");
            own_printf( "Test finished\n");
            exit(0);
        break;

    }
}

void mifare_icode_commands_execute(uint8_t *buff, size_t len)
{
    uint8_t cmd[4096];
    uint32_t val32;
    static uint8_t tag_count;

    srand(time(0));

    if (buff[0] == CMD_ERROR)
    {
        own_printf( "Command 0x%02X failed with ERROR 0x%02X%02X!!!\n", buff[1], buff[2], buff[3]);
    }
    else if (buff[0] == CMD_ACK)
    switch (buff[1])
    {
        case CMD_DUMMY_COMMAND:
            own_printf( "OK\n");
            cmd[0] = CMD_GET_TAG_COUNT;
            binary_protocol_send(cmd, 1);
            own_printf( "==> Get tag count = ");
        break;
        case CMD_GET_TAG_COUNT:
            tag_count = buff[2];
            own_printf( "%d\n", tag_count);
            if (tag_count > 0)
            {
                cmd[0] = CMD_GET_UID;
                cmd[1] = tag_count-1;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Get UID info: ");
            }
            else
            {
                cmd[0] = CMD_SET_POLLING;
                cmd[1] = 1;
                binary_protocol_send(cmd, 2);
                own_printf( "==> Enable polling - ");
            }
        break;
        case CMD_GET_UID:
            own_printf("type: 0x%02X, param: 0x%02X, UID len: 0x%02X, UID: ", buff[2], buff[3], len-4);
            for (uint8_t k=0; k< len-4; k++)
                own_printf( " 0x%02X", buff[k+4]);
            own_printf( "\n");

            cmd[0] = CMD_ICODE_WRITE_BLOCK;
            cmd[1] = 1;
            cmd[2] = 4;
            for (uint8_t k=0; k< 16; k++)
                cmd[k+3] = k;

            binary_protocol_send(cmd, 19);
            own_printf( "==> Write block: ");
        break;
        case CMD_ICODE_WRITE_BLOCK:
            own_printf( "OK\n");
            cmd[0] = CMD_ICODE_READ_BLOCK;
            cmd[1] = 1;
            cmd[2] = 4;

            binary_protocol_send(cmd, 3);
            own_printf( "==> Read block:");
        break;
        case CMD_ICODE_READ_BLOCK:
            for (uint8_t k=2; k< len; k++)
                own_printf( " 0x%02X", buff[k]);
            own_printf( "\n");

            cmd[0] = CMD_ICODE_GET_SYSTEM_INFORMATION;
            binary_protocol_send(cmd, 1);
            own_printf( "==> Get system information: ");
        break;
        case CMD_ICODE_GET_SYSTEM_INFORMATION:
            for (uint8_t k=2; k< len; k++)
                own_printf( " 0x%02X", buff[k]);
            own_printf( "\n");

            cmd[0] = CMD_ICODE_GET_MULTIPLE_BSS;
            cmd[1] = 0;
            cmd[2] = 10;
            binary_protocol_send(cmd, 3);
            own_printf( "==> Get multiple block security status: ");
        break;
        case CMD_ICODE_GET_MULTIPLE_BSS:
            for (uint8_t k=2; k< len; k++)
                own_printf( " 0x%02X", buff[k]);
            own_printf( "\n");

            cmd[0] = CMD_SET_POLLING;
            cmd[1] = 1;
            binary_protocol_send(cmd, 2);
            own_printf( "==> Enable polling - ");
        break;
        case CMD_SET_POLLING:
            own_printf( "OK\n");
            own_printf( "Test finished\n");
            exit(0);
        break;
    }
}

void mifare_net_commands_execute(uint8_t *buff, size_t len)
{
    uint8_t cmd[4096];
    uint32_t val32;
    static uint8_t tag_count;

    srand(time(0));

    if (buff[0] == CMD_ERROR)
    {
        own_printf( "Command 0x%02X failed with ERROR 0x%02X%02X!!!\n", buff[1], buff[2], buff[3]);
    }
    else if (buff[0] == CMD_ACK)
    switch (buff[1])
    {
        case CMD_DUMMY_COMMAND:
            own_printf( "OK\n");
            cmd[0] = CMD_SET_NET_CFG;
            cmd[1] = 0x00; //mode
            cmd[2] = 0x01; //client
            binary_protocol_send(cmd, 3);
            own_printf( "==> Set mode to client: ");
        break;
        case CMD_SET_NET_CFG:
            own_printf( "OK\n");
            switch (buff[2])
            {
                case 0x00: //mode ACK
                    cmd[0] = CMD_SET_NET_CFG;
                    cmd[1] = 0x03; //ssid
                    strcpy((char*)&cmd[2], TEST_SSID);

                    binary_protocol_send(cmd, strlen(TEST_SSID) +2);
                    own_printf( "==> Set ssid to: %s - ", TEST_SSID);
                break;
                case 0x03: //ssid ACK
                    cmd[0] = CMD_SET_NET_CFG;
                    cmd[1] = 0x04; //wifi password
                    strcpy((char*)&cmd[2], TEST_PASSWORD);

                    binary_protocol_send(cmd, strlen(TEST_PASSWORD) +2);
                    own_printf( "==> Set wifi password - ");
                break;
                case 0x04: //password ACK
                    cmd[0] = CMD_SET_NET_CFG;
                    cmd[1] = 0x05; //fixed ip
                    cmd[2] = 0x01; //on

                    binary_protocol_send(cmd, 3);
                    own_printf( "==> Disabling DHCP: ");
                break;
                case 0x05: //fixed ip ACK
                    cmd[0] = CMD_SET_NET_CFG;
                    cmd[1] = 0x06; //ip
                    cmd[2] = 172;
                    cmd[3] = 16;
                    cmd[4] = 16;
                    cmd[5] = 62;

                    binary_protocol_send(cmd, 6);
                    own_printf( "==> Setting IP address to %d.%d.%d.%d: ", cmd[2], cmd[3], cmd[4], cmd[5]);
                break;

                case 0x06: //ip ACK
                    cmd[0] = CMD_SET_NET_CFG;
                    cmd[1] = 0x07; //netmask
                    cmd[2] = 255;
                    cmd[3] = 255;
                    cmd[4] = 255;
                    cmd[5] = 0;

                    binary_protocol_send(cmd, 6);
                    own_printf( "==> Setting netmask to %d.%d.%d.%d: ", cmd[2], cmd[3], cmd[4], cmd[5]);
                break;
                case 0x07: //netmask ACK
                    cmd[0] = CMD_SET_NET_CFG;
                    cmd[1] = 0x08; //gateway
                    cmd[2] = 172;
                    cmd[3] = 16;
                    cmd[4] = 16;
                    cmd[5] = 6;

                    binary_protocol_send(cmd, 6);
                    own_printf( "==> Setting gateway to %d.%d.%d.%d: ", cmd[2], cmd[3], cmd[4], cmd[5]);
                break;
                case 0x08: //gateway ACK
                    cmd[0] = CMD_SET_NET_CFG;
                    cmd[1] = 0x09; //dns
                    cmd[2] = 8;
                    cmd[3] = 8;
                    cmd[4] = 8;
                    cmd[5] = 8;

                    binary_protocol_send(cmd, 6);
                    own_printf( "==> Setting DNS to %d.%d.%d.%d: ", cmd[2], cmd[3], cmd[4], cmd[5]);
                break;
                case 0x09: //dhcp ACK
                    cmd[0] = CMD_REBOOT;
                    binary_protocol_send(cmd, 1);
                    own_printf( "==> Rebooting device: ");
                break;
            }
        break;
        case CMD_REBOOT:
            own_printf( "OK\n");
            own_printf( "Test finished\n");
            exit(0);
        break;
    }
}


void uid_commands_execute(uint8_t *buff, size_t len)
{
    uint8_t cmd[4096];
    uint32_t val32;
    static uint8_t tag_count;

    srand(time(0));

    if (buff[0] == CMD_ERROR)
    {
        own_printf( "Command 0x%02X failed with ERROR 0x%02X%02X!!!\n", buff[1], buff[2], buff[3]);
    }
    else if (buff[0] == CMD_ACK)
    switch (buff[1])
    {
        case CMD_DUMMY_COMMAND:
            cmd[0] = CMD_GET_TAG_COUNT;
            binary_protocol_send(cmd, 1);
        break;
        case CMD_GET_TAG_COUNT:
            tag_count = buff[2];
            if (tag_count > 0)
            {
                cmd[0] = CMD_GET_UID;
                cmd[1] = tag_count-1;
                binary_protocol_send(cmd, 2);
            }
            else
            {
                cmd[0] = CMD_HALT;
                binary_protocol_send(cmd, 1);
                own_printf( "NO_TAG\n");
            }
        break;
        case CMD_GET_UID:
            for (uint8_t k=0; k< len-4; k++)
                own_printf( "%02X", buff[k+4]);
            own_printf( "\n");

            cmd[0] = CMD_HALT;

            binary_protocol_send(cmd, 1);
        break;
	case CMD_HALT:
            exit(0);
        break;
    }
}



/**
    @brief Function prints help
*/
void print_usage()
{
  own_printf( "\nUsage: c1-tool [device path] [command]\n");
  own_printf( "\nDevice path for serial device like /dev/ttyS1 or /dev/ttyUSB0\n");
  own_printf( "or if the TCP server is enabled in the device the device path should be as IP:PORT eg. 192.168.1.111:1234\n\n");
  own_printf( "Available commands:\n");
  own_printf( " mc       - perform test on Mifare Clasics tag\n");
  own_printf( " mul      - perform test on Mifare Ultralight tag\n");
  own_printf( " mdf      - perform test on Mifare Desfire tag\n");
  own_printf( " ic       - perform test on ICODE tag\n");
  own_printf( " net      - network configurtion test\n");
  own_printf( " uid      - gets the UID only - usefull for scripts\n");

  if (serial_fd != -1)
    close(serial_fd);
  own_printf( "\n");
  exit(EXIT_FAILURE);
}

void loop_test(void)
{
    fd_set rfds;
    struct timeval tv;
    int retval, fdmax, lenght;
    uint8_t buff[1024];
    uint8_t cmd[20];

    cmd[0] = CMD_DUMMY_COMMAND;
    binary_protocol_send(cmd, 1);
    if (silent_mode == 0)
    {
	own_printf( "==> Dummy command: ");
    }
    while (1)
    {
        tv.tv_sec = 1;
        tv.tv_usec = 0;
        FD_ZERO(&rfds);
        FD_SET(serial_fd, &rfds);

        fdmax = serial_fd;

        retval = select(fdmax+1, &rfds, NULL, NULL, &tv);

        if (retval == -1)
        {
          perror("select()");
          own_printf("select(serial_fd: %d)\n", serial_fd);
          return;
        }
        else if (retval)
        {
          if (FD_ISSET(serial_fd, &rfds))
          {
            do
            {
              lenght = read(serial_fd, buff, sizeof(buff));
              if (lenght > 0)
              {
                binary_protocol_parse(buff, lenght);
              }
            } while (lenght > 0);
          }
        }
        else
        {
          own_printf("Timeout - check connection to the board and device configuration\n");
	  return;
        }
    }
}

int parse_commands(int argc, char * argv[])
{
    if (strcmp(argv[2], "mc") == 0)
    {
        own_printf("Running Mifare test...\n");
        binary_protocol_init(mifare_commands_execute, uart_protocol_write);
        loop_test();
    }
    else if (strcmp(argv[2], "mul") == 0)
    {
        own_printf("Running Mifare Ultralight test...\n");
        binary_protocol_init(mifare_ul_commands_execute, uart_protocol_write);
        loop_test();
    }
    else if (strcmp(argv[2], "mdf") == 0)
    {
        own_printf("Running Mifare Desfire test...\n");
        binary_protocol_init(mifare_df_commands_execute, uart_protocol_write);
        loop_test();
    }
    else if (strcmp(argv[2], "ic") == 0)
    {
        own_printf("Running ICODE test...\n");
        binary_protocol_init(mifare_icode_commands_execute, uart_protocol_write);
        loop_test();
    }
    else if (strcmp(argv[2], "net") == 0)
    {
        own_printf("Running netowrk set test...\n");
        binary_protocol_init(mifare_net_commands_execute, uart_protocol_write);
        loop_test();
    }
    else if (strcmp(argv[2], "uid") == 0)
    {
	silent_mode = 1;
        binary_protocol_init(uid_commands_execute, uart_protocol_write);
        loop_test();
    }
    else
        print_usage();

    return -1;
}

static int setargs(char *args, char **argv)
{
    int count = 0;

    while (isspace(*args))
        ++args;
    while (*args)
    {
        if (argv)
            argv[count] = args;
        while (*args && !isspace(*args))
            ++args;
        if (argv && *args)
            *args++ = '\0';
        while (isspace(*args))
            ++args;
        count++;
    }
    return count;
}

char **parsedargs(char *args, int *argc)
{
    char **argv = NULL;
    int    argn = 0;

    if (args && *args
            && (args = strdup(args))
            && (argn = setargs(args,NULL))
            && (argv = malloc((argn+1) * sizeof(char *))))
    {
        *argv++ = args;
        argn = setargs(args,argv);
    }

    if (args && !argv)
        free(args);

    *argc = argn;
    return argv;
}

void freeparsedargs(char **argv)
{
    if (argv)
    {
        free(argv[-1]);
        free(argv-1);
    }
}

int main(int argc, char * argv[])
{
    uint8_t rxBuff[MAX_FRAME_SIZE * 2];
    uint8_t txBuff[MAX_FRAME_SIZE * 2];
    uint16_t lenght;
    int optind, res;

    if (argc < 3)
      print_usage();


    if (strncmp("/dev/", argv[1], 5) == 0)
        serial_fd = open_port(argv[1], 115200);
    else
        serial_fd = open_socket(argv[1]);

    if (serial_fd == -1)
    {
        own_printf("Unable to open connection over %s for C1 module!\n", argv[1]);
        return -1;
    }

    res = parse_commands(argc, argv);

    if (serial_fd != -1)
        close(serial_fd);

    return res;
}
