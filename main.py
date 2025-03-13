import variableHolster as vH
import character as cH
import pygame as pg

#Main Function
def main():
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
    baseInputCollection()
    cH.drawBackground()
    cH.movePlayer()
    cH.drawPlayer()


    paintAndClearScreen(vH.backgroundColor)

    
def runTitle():
    baseInputCollection()



    paintAndClearScreen(vH.backgroundColor)
    
    
def runLeveling():
    baseInputCollection()


    paintAndClearScreen(vH.backgroundColor)
    
    
def baseInputCollection():
    vH.clock.tick(vH.frameRate)
    vH.keys = pg.key.get_pressed()
    for event in pg.event.get():
        if event.type == pg.QUIT: vH.done = True  # Close the entire program when windows x is clicked
        if event.type == pg.MOUSEBUTTONDOWN: vH.mouseDown = True #Click logic
        if event.type == pg.MOUSEBUTTONUP: vH.mouseDown = False
    if vH.keys[pg.K_ESCAPE]: vH.done = True
    vH.mouseX, vH.mouseY = pg.mouse.get_pos() # Saves current mouse position

def paintAndClearScreen(color):
    pg.display.flip()  # Displays currently drawn frame
    vH.screen.fill(color)  # Clears screen with a black color
    
if __name__=="__main__":
    main()