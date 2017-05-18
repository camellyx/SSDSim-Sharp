ifeq ($(DEBUG),1)
	FLAGS=-d:DEBUG -debug
else
	FLAGS=
endif

Modules = .
SRC_DIR = $(addprefix ./, $(Modules))

ALL_SRC = $(foreach sdir, $(SRC_DIR), $(shell find $(sdir) -name '*.cs'))
SRC = $(filter-out $(EXCLUDES), $(ALL_SRC))
OBJ = bin/SSDSim-MultiFlow.exe

PHONY: build

build: $(OBJ)

$(OBJ): $(SRC)
	if [ ! -d bin ]; then mkdir bin; fi;
	dmcs -nowarn:0219 -r:System.Xml.dll -unsafe $(FLAGS) -out:$@ $^

clean:
	rm -rf $(OBJ) $(OBJ).mdb
