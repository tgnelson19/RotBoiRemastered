from enum import Enum

done = False
mouseDown = False

#Game States
class States(Enum):
    TITLESCREEN = 0
    GAMERUN = 1
    LEVELING = 2
    
state = States.TITLESCREEN