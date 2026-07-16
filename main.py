import variableHolster as vH
import character as cH
import pygame as pg
import characterStats as cS
import background as bG
import menus

CAMERA_ROTATION_DEGREES_PER_SECOND = 180.0

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
            case vH.States.PAUSED:
                runPaused()
            case vH.States.RESULTS:
                runResults()
        
def runGame():
    pg.mouse.set_visible(False)
    
    if vH.hasBeenReset:
        vH.hasBeenReset = False
    
    baseInputCollection() #Collects the inputs from the player
    if vH.state != vH.States.GAMERUN:
        return
    cS.runTimeSeconds += min(vH.deltaMilliseconds, 50) / 1000.0
    pg.mouse.set_visible(vH.mouseX >= cS.informationSheet.arena_width)
    
    cH.drawBackground() #Draws the base level background
    
    #Player Handling
    cH.movePlayer()
    #Bullet Handling
    cH.handlingBulletCreation()
    cH.handlingBulletUpdating()
    
    #Enemy Handling
    cH.handlingEnemyCreation()
    cH.handlingBossDebugControls()
    cH.handlingEnemyUpdatesAndDrawing()
    # Keep the player readable above enemy bodies while hostile projectiles and
    # telegraphs remain above the player as actionable threats.
    cH.drawPlayer()
    cH.handlingEnemyProjectileUpdating()
    cH.handlingDamagingEnemies()
    
    cH.updateDamageTexts()
    cH.updateExperience()
    cH.expForPlayer()
    cH.updateLootCrates()
    cH.crateInteractionForPlayer()
    cH.recoverPlayerHealth()
    cH.hurtPlayer()

    cH.drawBountyIndicator()
    cH.drawInformationSheet()

    #Paints and clears screen for next frame
    paintAndClearScreen(vH.backgroundColor)
    
def runLeveling():
    pg.mouse.set_visible(True)
    baseInputCollection()
    
    cH.handleLevelingProcess()
    
    paintAndClearScreen(vH.backgroundColor)

def runPaused():
    pg.mouse.set_visible(True)
    baseInputCollection()
    menus.draw_pause()
    menus.handle_pause()
    paintAndClearScreen(vH.backgroundColor)

def runResults():
    pg.mouse.set_visible(True)
    baseInputCollection()
    menus.draw_results()
    menus.handle_results()
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
    update_camera_controls()

    if pg.K_ESCAPE in vH.keyPressed:
        if vH.state == vH.States.GAMERUN:
            vH.pauseReturnState = vH.States.GAMERUN
            vH.state = vH.States.PAUSED
            _cancel_drag()
        elif vH.state == vH.States.LEVELING:
            vH.pauseReturnState = vH.States.LEVELING
            vH.state = vH.States.PAUSED
            _cancel_drag()

    vH.mouseX, vH.mouseY = pg.mouse.get_pos()  # Save mouse position for aim and clicks


def _cancel_drag():
    """Clear any in-progress equipment drag so pausing never leaves firing suspended."""
    vH.dragInProgress = False
    sheet = getattr(cS, "informationSheet", None)
    if sheet is not None:
        sheet.dragging_item = None
        sheet.dragging_source = None


def update_input_toggles():
    """Toggle persistent gameplay assists once per key press."""
    if (pg.K_b in vH.keyPressed and cS.activeBoss is None
            and not cS.bossDebugRequested):
        cS.bossDebugRequested = True
    if pg.K_i in vH.keyPressed:
        cS.autoFire = not cS.autoFire
        import gameProfile
        gameProfile.profile["autofire"] = cS.autoFire
        gameProfile.save_profile()
    if pg.K_TAB in vH.keyPressed and vH.state == vH.States.GAMERUN:
        cS.informationSheet.toggle_mode()
    if pg.K_y in vH.keyPressed:
        cS.bossDebugInvincible = not cS.bossDebugInvincible


def update_camera_controls():
    """Apply held Q/E camera input at a frame-rate-independent angular speed."""
    if vH.state != vH.States.GAMERUN:
        return
    direction = int(bool(vH.keys[pg.K_e])) - int(bool(vH.keys[pg.K_q]))
    if direction:
        elapsed_seconds = min(max(vH.deltaMilliseconds, 0), 50) / 1000.0
        bG.rotate_camera(
            direction * CAMERA_ROTATION_DEGREES_PER_SECOND * elapsed_seconds,
        )


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
        elif event.type == pg.JOYAXISMOTION:
            if event.axis == 0:
                vH.controllerMoveX = event.value if abs(event.value) > .2 else 0
            elif event.axis == 1:
                vH.controllerMoveY = event.value if abs(event.value) > .2 else 0
            elif event.axis == 2:
                vH.controllerAimX = event.value if abs(event.value) > .25 else 0
            elif event.axis == 3:
                vH.controllerAimY = event.value if abs(event.value) > .25 else 0
        elif event.type == pg.JOYBUTTONDOWN:
            if event.button == 0:
                vH.keyPressed.add(pg.K_SPACE)
            elif event.button == 2:
                vH.keyPressed.add(pg.K_i)
            elif event.button == 7 and vH.state == vH.States.GAMERUN:
                vH.pauseReturnState = vH.States.GAMERUN
                vH.state = vH.States.PAUSED


def paintAndClearScreen(color):
    pg.display.flip()  # Show the frame that was drawn this update
    vH.screen.fill(color)  # Clear screen for the next frame
    
if __name__=="__main__":
    main()
