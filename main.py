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
    pg.mouse.set_visible(False)
    
    if vH.hasBeenReset:
        vH.hasBeenReset = False
    
    baseInputCollection() #Collects the inputs from the player
    pg.mouse.set_visible(vH.mouseX >= vH.sW * .75)
    
    cH.drawBackground() #Draws the base level background
    
    #Player Handling
    cH.movePlayer()
    cH.drawPlayer()
    
    #Bullet Handling
    cH.handlingBulletCreation()
    cH.handlingBulletUpdating()
    
    #Enemy Handling
    cH.handlingEnemyCreation()
    cH.handlingBossDebugControls()
    cH.handlingEnemyUpdatesAndDrawing()
    cH.handlingEnemyProjectileUpdating()
    cH.handlingDamagingEnemies()
    
    cH.updateDamageTexts()
    cH.updateExperience()
    cH.expForPlayer()
    cH.hurtPlayer()
    
    cH.drawInformationSheet()

    #Paints and clears screen for next frame
    paintAndClearScreen(vH.backgroundColor)
    
def runLeveling():
    pg.mouse.set_visible(True)
    baseInputCollection()
    
    cH.handleLevelingProcess()
    
    paintAndClearScreen(vH.backgroundColor)
    
def runTitle():
    pg.mouse.set_visible(True)
    baseInputCollection()
    
    if not vH.hasBeenReset:
        cH.resetAllStats()
        vH.hasBeenReset = True
        
    cH.runTheTitleScreen()

    paintAndClearScreen(vH.backgroundColor)
    
def baseInputCollection():
    """Collect input state, handle toggles, mouse events, and quit requests."""
    vH.set_delta_time(vH.clock.tick(vH.frameRate))
    vH.keys = pg.key.get_pressed()
    vH.keyPressed.clear()
    vH.mousePressed = False

    handle_input_events()
    update_input_toggles()

    if vH.keys[pg.K_ESCAPE]:
        vH.done = True

    vH.mouseX, vH.mouseY = pg.mouse.get_pos()  # Save mouse position for aim and clicks


def update_input_toggles():
    """Toggle persistent gameplay assists once per key press."""
    if pg.K_i in vH.keyPressed:
        cS.autoFire = not cS.autoFire
    if pg.K_y in vH.keyPressed:
        cS.bossDebugInvincible = not cS.bossDebugInvincible


def handle_input_events():
    """Process pygame events and update global input state."""
    for event in pg.event.get():
        if event.type == pg.QUIT:
            vH.done = True
        elif event.type == pg.KEYDOWN:
            vH.keyPressed.add(event.key)
        elif event.type == pg.MOUSEBUTTONDOWN:
            if event.button == 1:
                vH.mouseDown = True
                vH.mousePressed = True
        elif event.type == pg.MOUSEBUTTONUP:
            if event.button == 1:
                vH.mouseDown = False


def paintAndClearScreen(color):
    pg.display.flip()  # Show the frame that was drawn this update
    vH.screen.fill(color)  # Clear screen for the next frame
    
if __name__=="__main__":
    main()
