CC=$(CROSS_COMPILE)gcc
CFLAGS=-I. -ggdb -O0

c1-tool: main.o binary_protocol.o
	$(CC) -o c1-tool main.o binary_protocol.o $(CFLAGS)

clean:
	rm -f main.o binary_protocol.o c1-tool

install:
	cp c1-tool /usr/bin
