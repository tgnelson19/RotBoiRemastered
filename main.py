import variableHolster as vH
import character as cH
import pygame as pg
import characterStats as cS

#Main State Machine Function
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
    
    if vH.hasBeenReset:
        vH.hasBeenReset = False
    
    baseInputCollection() #Collects the inputs from the player
    
    cH.drawBackground() #Draws the base level background
    
    #Player Handling
    cH.movePlayer()
    cH.drawPlayer()
    
    #Bullet Handling
    cH.handlingBulletCreation()
    cH.handlingBulletUpdating()
    
    #Enemy Handling
    cH.handlingEnemyCreation()
    cH.handlingEnemyUpdatesAndDrawing()
    cH.handlingDamagingEnemies()
    
    cH.updateDamageTexts()
    cH.updateExperience()
    cH.expForPlayer()
    cH.hurtPlayer()
    
    cH.drawInformationSheet()

    #Paints and clears screen for next frame
    paintAndClearScreen(vH.backgroundColor)
    
def runLeveling():
    baseInputCollection()
    
    cH.handleLevelingProcess()
    
    paintAndClearScreen(vH.backgroundColor)
    
def runTitle():
    baseInputCollection()
    
    if not vH.hasBeenReset:
        cH.resetAllStats()
        vH.hasBeenReset = True
        
    cH.runTheTitleScreen()

    paintAndClearScreen(vH.backgroundColor)
    
def baseInputCollection():
    """Collect input state, handle toggles, mouse events, and quit requests."""
    vH.clock.tick(vH.frameRate)
    vH.keys = pg.key.get_pressed()

    update_input_toggles()
    handle_input_events()

    if vH.keys[pg.K_ESCAPE]:
        vH.done = True

    vH.mouseX, vH.mouseY = pg.mouse.get_pos()  # Save mouse position for aim and clicks


def update_input_toggles():
    """Toggle autofire and light mode once per key press."""
    if vH.keys[pg.K_i]:
        if not cS.autoFire and not cS.autoFlop:
            cS.autoFire = True
            cS.autoFlop = True
        elif not cS.autoFlop:
            cS.autoFire = False
            cS.autoFlop = True
    else:
        cS.autoFlop = False

    if vH.keys[pg.K_o]:
        if not cS.lightSwitch and not cS.lightFlop:
            cS.lightSwitch = True
            cS.lightFlop = True
        elif not cS.lightFlop:
            cS.lightSwitch = False
            cS.lightFlop = True
    else:
        cS.lightFlop = False


def handle_input_events():
    """Process pygame events and update global input state."""
    for event in pg.event.get():
        if event.type == pg.QUIT:
            vH.done = True
        elif event.type == pg.MOUSEBUTTONDOWN:
            vH.mouseDown = True
        elif event.type == pg.MOUSEBUTTONUP:
            vH.mouseDown = False


def paintAndClearScreen(color):
    pg.display.flip()  # Show the frame that was drawn this update
    vH.screen.fill(color)  # Clear screen for the next frame
    
if __name__=="__main__":
    main()