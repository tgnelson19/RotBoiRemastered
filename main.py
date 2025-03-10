from os import environ
import variableHolster as vH
import pygame


#Main Function
def main():
    environ['SDL_VIDEO_CENTERED'] = '1'
    pygame.init()  # Initializes a window
    pygame.display.set_caption("RotBoiRemastered")
    
    while not vH.done:
        match vH.state:
            #This state is the title screen
            case vH.States.GAMERUN:
                runGame()
            #This state is when the game runs
            case vH.States.TITLESCREEN: 
                runTitle() 
            #This state is when the player reaches a level up
            case vH.States.LEVELING:
                runLeveling()
        
        
def runGame():
    inputCollection()
    print("game")
    
def runTitle():
    inputCollection()
    print("title")
    
def runLeveling():
    inputCollection()
    print("leveling")
    
def inputCollection():
    for event in pygame.event.get(): 
            if event.type == pygame.QUIT: vH.done = True  # Close the entire program when windows x is clicked
            if event.type == pygame.MOUSEBUTTONDOWN: vH.mouseDown = True #Click logic
            if event.type == pygame.MOUSEBUTTONUP: vH.mouseDown = False
    
    
if __name__=="__main__":
    main()